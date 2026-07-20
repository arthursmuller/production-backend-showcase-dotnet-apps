namespace SecretProvider;

/// <summary>
/// Identifies one secret in a provider-owned namespace.
/// </summary>
public sealed record SecretReference
{
    public SecretReference(string secretNamespace, string key)
    {
        if (string.IsNullOrWhiteSpace(secretNamespace))
            throw new ArgumentException("A secret namespace is required.", nameof(secretNamespace));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A secret key is required.", nameof(key));

        Namespace = secretNamespace;
        Key = key;
    }

    public string Namespace { get; }

    public string Key { get; }

    public override string ToString() => $"{Namespace}/{Key}";
}
