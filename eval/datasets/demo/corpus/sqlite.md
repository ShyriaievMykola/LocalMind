# SQLite notes

SQLite can run in WAL mode (`PRAGMA journal_mode=WAL`): readers do not block the writer and the writer does not
block readers, which suits a desktop app with a background indexer. The FTS5 extension adds full-text search with
BM25 ranking, so keyword queries over many text chunks stay fast without an external search server.
