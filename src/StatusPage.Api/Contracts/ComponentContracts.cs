using System.ComponentModel.DataAnnotations;

namespace StatusPage.Api.Contracts;

/// <summary>A component as the operator console sees it.</summary>
public sealed record ComponentResponse(
    Guid Id,
    string Name,
    string Slug,
    string TargetUrl,
    int ExpectedStatusCode,
    int DegradedAboveMs,
    int FailuresToOpen,
    int SuccessesToClose,
    bool Enabled,
    int Position);

/// <summary>What an operator must supply to add a component.</summary>
public sealed record CreateComponentRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public required string Name { get; init; }

    [Required, StringLength(80, MinimumLength = 1)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$",
        ErrorMessage = "A slug is lowercase letters, digits and single hyphens between them.")]
    public required string Slug { get; init; }

    [Required, StringLength(2048, MinimumLength = 1)]
    public required string TargetUrl { get; init; }

    [Range(100, 599)]
    public int ExpectedStatusCode { get; init; } = 200;

    [Range(0, 600_000)]
    public int DegradedAboveMs { get; init; } = 500;

    [Range(1, 100)]
    public int FailuresToOpen { get; init; } = 3;

    [Range(1, 100)]
    public int SuccessesToClose { get; init; } = 2;

    public bool Enabled { get; init; } = true;

    [Range(0, 10_000)]
    public int Position { get; init; }
}

/// <summary>A whole-component replacement. The slug is not editable; see the API docs.</summary>
public sealed record UpdateComponentRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public required string Name { get; init; }

    [Required, StringLength(2048, MinimumLength = 1)]
    public required string TargetUrl { get; init; }

    [Range(100, 599)]
    public int ExpectedStatusCode { get; init; } = 200;

    [Range(0, 600_000)]
    public int DegradedAboveMs { get; init; } = 500;

    [Range(1, 100)]
    public int FailuresToOpen { get; init; } = 3;

    [Range(1, 100)]
    public int SuccessesToClose { get; init; } = 2;

    public bool Enabled { get; init; } = true;

    [Range(0, 10_000)]
    public int Position { get; init; }
}
