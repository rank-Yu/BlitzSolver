namespace OfflineHost;

/// <summary>输入包含本批不认识或明确不支持的值；调用方必须处理为失败。</summary>
internal sealed class UnsupportedException(string message) : Exception(message);
