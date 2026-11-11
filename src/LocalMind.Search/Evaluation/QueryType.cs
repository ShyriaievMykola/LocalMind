namespace LocalMind.Search.Evaluation;

/// <summary>Тип запиту в датасеті: від нього залежить, який режим пошуку має виграти.</summary>
public enum QueryType
{
    /// <summary>Точний збіг слів і термінів (<c>exact</c>) — природна територія BM25.</summary>
    Exact,

    /// <summary>Те саме іншими словами (<c>paraphrase</c>) — те, для чого потрібні embeddings.</summary>
    Paraphrase,

    /// <summary>Запит однією мовою, документ іншою (<c>crossLingual</c>).</summary>
    CrossLingual,
}
