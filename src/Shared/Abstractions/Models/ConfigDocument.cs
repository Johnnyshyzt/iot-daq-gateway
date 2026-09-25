namespace Gateway.Abstractions.Models;

/// <summary>
/// One YAML resource in the draft or published bundle.
/// </summary>
public sealed record ConfigDocument
{
    /// <summary>Bundle-relative path with forward slashes, such as <c>devices/cnc-01.yaml</c>.</summary>
    public required string RelativePath { get; init; }

    /// <summary>UTF-8 YAML text of a single resource.</summary>
    public required string Yaml { get; init; }
}

/// <summary>
/// One schema or cross-file problem. <see cref="Path"/> is a bundle-relative
/// file path, optionally followed by a JSON Pointer (<c>devices/cnc-01.yaml#/spec/adapter</c>).
/// </summary>
public sealed record ConfigValidationIssue
{
    public required string Path { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }
}

/// <summary>
/// Outcome of <c>IConfigPublisher.ValidateDraftAsync</c>.
/// <see cref="Valid"/> is true only when <see cref="Issues"/> is empty.
/// </summary>
public sealed record ConfigValidationResult
{
    public required bool Valid { get; init; }

    public required IReadOnlyList<ConfigValidationIssue> Issues { get; init; }
}
