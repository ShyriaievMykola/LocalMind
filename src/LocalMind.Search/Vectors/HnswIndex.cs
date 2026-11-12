using System.Buffers;
using LocalMind.Core.Abstractions;
using LocalMind.Core.Models;
using LocalMind.Search.Configuration;

namespace LocalMind.Search.Vectors;

/// <summary>
/// Наближений пошук найближчих векторів за алгоритмом HNSW (ієрархічний граф «малого світу»).
/// Вектори мають бути L2-нормалізованими: схожість — скалярний добуток, відстань — 1 − схожість.
/// Паралельні пошуки не блокують один одного; додавання і видалення виконуються ексклюзивно.
/// Видалення справжнє (без «надгробків»): зв'язки сусідів відновлюються, тож граф не засмічується.
/// </summary>
public sealed class HnswIndex : IVectorIndex, IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly Dictionary<Guid, int> _slots = [];
    private readonly Stack<int> _freeSlots = new();
    private readonly int _m;
    private readonly int _efConstruction;
    private readonly int _efSearch;
    private readonly double _levelMultiplier;
    private readonly Random _random;

    private Guid[] _ids = new Guid[16];
    private float[]?[] _vectors = new float[]?[16];
    private List<int>[]?[] _links = new List<int>[]?[16];
    private int _usedSlots;
    private int _entryPoint = -1;
    private int _topLevel = -1;

    /// <param name="dimensions">Розмірність векторів, більше нуля.</param>
    /// <param name="options">Параметри M, efConstruction, efSearch (див. <see cref="VectorIndexOptionsValidator"/>).</param>
    /// <param name="seed">Зерно генератора рівнів: з тим самим зерном і порядком додавання граф однаковий.</param>
    public HnswIndex(int dimensions, VectorIndexOptions options, int seed = 42)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        ArgumentNullException.ThrowIfNull(options);
        var validation = new VectorIndexOptionsValidator().Validate(null, options);
        if (validation.Failed)
        {
            throw new ArgumentOutOfRangeException(nameof(options), validation.FailureMessage);
        }

        Dimensions = dimensions;
        _m = options.M;
        _efConstruction = options.EfConstruction;
        _efSearch = options.EfSearch;
        _levelMultiplier = 1.0 / Math.Log(options.M);
        _random = new Random(seed);
    }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _slots.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <inheritdoc />
    public void Add(Guid chunkId, ReadOnlyMemory<float> vector)
    {
        EnsureDimensions(vector.Length, nameof(vector));
        EnsureFinite(vector.Span, nameof(vector));
        var copy = vector.ToArray();

        _lock.EnterWriteLock();
        try
        {
            if (_slots.ContainsKey(chunkId))
            {
                RemoveCore(chunkId);
            }

            Insert(chunkId, copy);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public bool Remove(Guid chunkId)
    {
        _lock.EnterWriteLock();
        try
        {
            return RemoveCore(chunkId);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Видаляє кілька векторів одним проходом по графі: коштує як одне <see cref="Remove"/>, а не як їх сума.
    /// Вектори, яких немає в індексі, пропускаються.
    /// </summary>
    /// <returns>Скільки векторів було видалено.</returns>
    public int RemoveRange(IEnumerable<Guid> chunkIds)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);

        _lock.EnterWriteLock();
        try
        {
            return RemoveNodes(chunkIds);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ScoredChunk> Search(ReadOnlySpan<float> query, int k)
    {
        EnsureDimensions(query.Length, nameof(query));
        EnsureFinite(query, nameof(query));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        _lock.EnterReadLock();
        try
        {
            if (_entryPoint < 0)
            {
                return [];
            }

            var entry = _entryPoint;
            for (var layer = _topLevel; layer > 0; layer--)
            {
                entry = GreedyClosest(query, entry, layer);
            }

            var found = SearchLayer(query, [entry], Math.Max(_efSearch, k), 0);
            var count = Math.Min(k, found.Count);
            var results = new ScoredChunk[count];
            for (var i = 0; i < count; i++)
            {
                results[i] = new ScoredChunk(_ids[found[i].Node], 1f - found[i].Distance);
            }

            // Рівні схожості впорядковуємо так само, як BruteForceIndex: за меншим ідентифікатором.
            Array.Sort(results, static (left, right) =>
            {
                var byScore = right.Score.CompareTo(left.Score);
                return byScore != 0 ? byScore : left.ChunkId.CompareTo(right.ChunkId);
            });
            return results;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    private int MaxLinks(int layer) => layer == 0 ? _m * 2 : _m;

    private float Distance(ReadOnlySpan<float> left, int node) => 1f - VectorMath.DotProduct(left, _vectors[node]!);

    private float Distance(int left, int right) => 1f - VectorMath.DotProduct(_vectors[left]!, _vectors[right]!);

    private void Insert(Guid chunkId, float[] vector)
    {
        var node = AllocateSlot();
        var level = (int)(-Math.Log(1.0 - _random.NextDouble()) * _levelMultiplier);
        _ids[node] = chunkId;
        _vectors[node] = vector;
        var links = new List<int>[level + 1];
        for (var layer = 0; layer <= level; layer++)
        {
            links[layer] = new List<int>(MaxLinks(layer) + 1);
        }

        _links[node] = links;
        _slots[chunkId] = node;

        if (_entryPoint < 0)
        {
            _entryPoint = node;
            _topLevel = level;
            return;
        }

        var entry = _entryPoint;
        for (var layer = _topLevel; layer > level; layer--)
        {
            entry = GreedyClosest(vector, entry, layer);
        }

        List<int> entries = [entry];
        for (var layer = Math.Min(level, _topLevel); layer >= 0; layer--)
        {
            var candidates = SearchLayer(vector, entries, _efConstruction, layer);
            var neighbors = SelectNeighbors(candidates, _m, exclude: node);
            links[layer].AddRange(neighbors);
            foreach (var neighbor in neighbors)
            {
                var back = _links[neighbor]![layer];
                back.Add(node);
                if (back.Count > MaxLinks(layer))
                {
                    Shrink(neighbor, layer, back);
                }
            }

            entries = [.. candidates.Select(c => c.Node)];
        }

        if (level > _topLevel)
        {
            _entryPoint = node;
            _topLevel = level;
        }
    }

    private bool RemoveCore(Guid chunkId) => RemoveNodes([chunkId]) == 1;

    /// <summary>Видаляє вузли одним проходом по графі й повертає, скільки з ідентифікаторів було в індексі.</summary>
    private int RemoveNodes(IEnumerable<Guid> chunkIds)
    {
        var removed = new HashSet<int>();
        foreach (var chunkId in chunkIds)
        {
            if (_slots.Remove(chunkId, out var node))
            {
                removed.Add(node);
            }
        }

        if (removed.Count == 0)
        {
            return 0;
        }

        // Зв'язки спрямовані, тож усіх, хто посилається на видалені вузли, знаходимо переглядом графа: O(n·M)
        // за один прохід незалежно від кількості видалених.
        for (var other = 0; other < _usedSlots; other++)
        {
            var otherLinks = _links[other];
            if (otherLinks is null || removed.Contains(other))
            {
                continue;
            }

            for (var layer = 0; layer < otherLinks.Length; layer++)
            {
                var links = otherLinks[layer];
                if (links.Exists(removed.Contains))
                {
                    Repair(other, layer, links, removed);
                }
            }
        }

        foreach (var node in removed)
        {
            _links[node] = null;
            _vectors[node] = null;
            _freeSlots.Push(node);
        }

        if (_slots.Count == 0)
        {
            _entryPoint = -1;
            _topLevel = -1;
        }
        else if (removed.Contains(_entryPoint))
        {
            ChooseNewEntryPoint();
        }

        return removed.Count;
    }

    /// <summary>Замість втрачених зв'язків вузол може узяти сусідів видалених вузлів.</summary>
    private void Repair(int owner, int layer, List<int> links, HashSet<int> removed)
    {
        var pool = new List<(int Node, float Distance)>(links.Count * 2);
        var seen = new HashSet<int> { owner };
        foreach (var link in links)
        {
            if (!removed.Contains(link) && seen.Add(link))
            {
                pool.Add((link, Distance(owner, link)));
            }
        }

        foreach (var link in links)
        {
            if (!removed.Contains(link))
            {
                continue;
            }

            var removedNeighbors = _links[link]!;
            if (layer >= removedNeighbors.Length)
            {
                continue;
            }

            foreach (var candidate in removedNeighbors[layer])
            {
                if (!removed.Contains(candidate) && seen.Add(candidate))
                {
                    pool.Add((candidate, Distance(owner, candidate)));
                }
            }
        }

        pool.Sort(static (left, right) => left.Distance.CompareTo(right.Distance));
        links.Clear();
        links.AddRange(SelectNeighbors(pool, MaxLinks(layer), exclude: owner));
    }

    private void ChooseNewEntryPoint()
    {
        var bestNode = -1;
        var bestLevel = -1;
        for (var node = 0; node < _usedSlots; node++)
        {
            var links = _links[node];
            if (links is not null && links.Length - 1 > bestLevel)
            {
                bestLevel = links.Length - 1;
                bestNode = node;
            }
        }

        _entryPoint = bestNode;
        _topLevel = bestLevel;
    }

    private void Shrink(int node, int layer, List<int> links)
    {
        var pool = new List<(int Node, float Distance)>(links.Count);
        foreach (var neighbor in links)
        {
            pool.Add((neighbor, Distance(node, neighbor)));
        }

        pool.Sort(static (left, right) => left.Distance.CompareTo(right.Distance));
        links.Clear();
        links.AddRange(SelectNeighbors(pool, MaxLinks(layer), exclude: node));
    }

    /// <summary>
    /// Евристика вибору сусідів: кандидат береться, лише якщо він ближчий до вузла, ніж до вже обраних, —
    /// так зв'язки йдуть у різні боки, а не збираються в один кластер. Решту місць добирають найближчими з відкинутих.
    /// </summary>
    private List<int> SelectNeighbors(List<(int Node, float Distance)> sortedCandidates, int count, int exclude)
    {
        var selected = new List<int>(count);
        List<int>? skipped = null;
        foreach (var (candidate, distance) in sortedCandidates)
        {
            if (selected.Count >= count)
            {
                break;
            }

            if (candidate == exclude)
            {
                continue;
            }

            var diverse = true;
            foreach (var chosen in selected)
            {
                if (Distance(candidate, chosen) < distance)
                {
                    diverse = false;
                    break;
                }
            }

            if (diverse)
            {
                selected.Add(candidate);
            }
            else
            {
                (skipped ??= []).Add(candidate);
            }
        }

        if (skipped is not null)
        {
            foreach (var candidate in skipped)
            {
                if (selected.Count >= count)
                {
                    break;
                }

                selected.Add(candidate);
            }
        }

        return selected;
    }

    private int GreedyClosest(ReadOnlySpan<float> query, int entry, int layer)
    {
        var current = entry;
        var currentDistance = Distance(query, current);
        bool improved;
        do
        {
            improved = false;
            foreach (var neighbor in _links[current]![layer])
            {
                var distance = Distance(query, neighbor);
                if (distance < currentDistance)
                {
                    current = neighbor;
                    currentDistance = distance;
                    improved = true;
                }
            }
        }
        while (improved);

        return current;
    }

    /// <summary>Пошук найближчих у шарі; результат відсортований за зростанням відстані.</summary>
    private List<(int Node, float Distance)> SearchLayer(ReadOnlySpan<float> query, List<int> entries, int ef, int layer)
    {
        var visited = ArrayPool<bool>.Shared.Rent(_usedSlots);
        Array.Clear(visited, 0, _usedSlots);
        try
        {
            var candidates = new PriorityQueue<int, float>();
            var best = new PriorityQueue<int, float>(); // пріоритет — мінус відстань: на вершині найгірший із знайдених
            var distances = new Dictionary<int, float>();
            foreach (var entry in entries)
            {
                var distance = Distance(query, entry);
                visited[entry] = true;
                distances[entry] = distance;
                candidates.Enqueue(entry, distance);
                best.Enqueue(entry, -distance);
                if (best.Count > ef)
                {
                    best.Dequeue();
                }
            }

            while (candidates.TryDequeue(out var current, out var currentDistance))
            {
                best.TryPeek(out _, out var worstNegated);
                if (best.Count >= ef && currentDistance > -worstNegated)
                {
                    break;
                }

                foreach (var neighbor in _links[current]![layer])
                {
                    if (visited[neighbor])
                    {
                        continue;
                    }

                    visited[neighbor] = true;
                    var distance = Distance(query, neighbor);
                    best.TryPeek(out _, out worstNegated);
                    if (best.Count < ef || distance < -worstNegated)
                    {
                        distances[neighbor] = distance;
                        candidates.Enqueue(neighbor, distance);
                        best.Enqueue(neighbor, -distance);
                        if (best.Count > ef)
                        {
                            best.Dequeue();
                        }
                    }
                }
            }

            var result = new List<(int Node, float Distance)>(best.Count);
            while (best.TryDequeue(out var node, out _))
            {
                result.Add((node, distances[node]));
            }

            result.Reverse();
            return result;
        }
        finally
        {
            ArrayPool<bool>.Shared.Return(visited);
        }
    }

    private int AllocateSlot()
    {
        if (_freeSlots.TryPop(out var free))
        {
            return free;
        }

        if (_usedSlots == _ids.Length)
        {
            var size = _ids.Length * 2;
            Array.Resize(ref _ids, size);
            Array.Resize(ref _vectors, size);
            Array.Resize(ref _links, size);
        }

        return _usedSlots++;
    }

    private static void EnsureFinite(ReadOnlySpan<float> vector, string parameterName)
    {
        foreach (var value in vector)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException("Вектор містить NaN або нескінченність.", parameterName);
            }
        }
    }

    private void EnsureDimensions(int length, string parameterName)
    {
        if (length != Dimensions)
        {
            throw new ArgumentException(
                $"Довжина вектора ({length}) не дорівнює розмірності індексу ({Dimensions}).",
                parameterName);
        }
    }
}
