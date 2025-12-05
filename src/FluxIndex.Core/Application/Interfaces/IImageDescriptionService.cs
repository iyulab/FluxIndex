using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Service for generating text descriptions of images using LLM vision capabilities.
/// </summary>
public interface IImageDescriptionService
{
    /// <summary>
    /// Generates a text description for an image.
    /// </summary>
    /// <param name="imageData">Raw image bytes.</param>
    /// <param name="mimeType">MIME type of the image.</param>
    /// <param name="context">Optional context about the document the image is from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated description.</returns>
    Task<ImageDescriptionResult> DescribeImageAsync(
        byte[] imageData,
        string mimeType,
        string? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a text description for an image from a URL.
    /// </summary>
    /// <param name="imageUrl">URL of the image.</param>
    /// <param name="context">Optional context about the document the image is from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated description.</returns>
    Task<ImageDescriptionResult> DescribeImageFromUrlAsync(
        string imageUrl,
        string? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates descriptions for multiple images in batch.
    /// </summary>
    /// <param name="images">List of image data with context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of descriptions.</returns>
    Task<IEnumerable<ImageDescriptionResult>> DescribeImagesAsync(
        IEnumerable<ImageDescriptionRequest> images,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts structured data from an image (e.g., text from charts, tables).
    /// </summary>
    /// <param name="imageData">Raw image bytes.</param>
    /// <param name="mimeType">MIME type of the image.</param>
    /// <param name="extractionType">Type of data to extract.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted structured data.</returns>
    Task<ImageDataExtractionResult> ExtractDataFromImageAsync(
        byte[] imageData,
        string mimeType,
        ImageDataExtractionType extractionType = ImageDataExtractionType.All,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the service is available (LLM with vision support is configured).
    /// </summary>
    /// <returns>True if image description is available.</returns>
    bool IsAvailable { get; }
}

/// <summary>
/// Request for image description.
/// </summary>
public class ImageDescriptionRequest
{
    /// <summary>
    /// Unique identifier for the request.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Raw image bytes.
    /// </summary>
    public byte[] ImageData { get; set; } = [];

    /// <summary>
    /// MIME type of the image.
    /// </summary>
    public string MimeType { get; set; } = "image/png";

    /// <summary>
    /// Optional context about the document.
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// Custom prompt for description generation.
    /// </summary>
    public string? CustomPrompt { get; set; }

    /// <summary>
    /// Maximum length of the description.
    /// </summary>
    public int MaxLength { get; set; } = 500;
}

/// <summary>
/// Result of image description generation.
/// </summary>
public class ImageDescriptionResult
{
    /// <summary>
    /// Request ID this result corresponds to.
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// Generated description of the image.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Brief summary suitable for alt text.
    /// </summary>
    public string? AltText { get; set; }

    /// <summary>
    /// Detected content type (chart, diagram, photo, screenshot, etc.).
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Confidence score for the description (0.0-1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Keywords extracted from the image.
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// Whether the description was successfully generated.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if generation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Tokens used for this generation.
    /// </summary>
    public int TokensUsed { get; set; }

    /// <summary>
    /// Time taken to generate the description.
    /// </summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Type of data to extract from an image.
/// </summary>
[Flags]
public enum ImageDataExtractionType
{
    None = 0,
    Text = 1,
    Tables = 2,
    Charts = 4,
    Diagrams = 8,
    Code = 16,
    All = Text | Tables | Charts | Diagrams | Code
}

/// <summary>
/// Result of structured data extraction from an image.
/// </summary>
public class ImageDataExtractionResult
{
    /// <summary>
    /// Whether extraction was successful.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if extraction failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Extracted text content.
    /// </summary>
    public string? ExtractedText { get; set; }

    /// <summary>
    /// Extracted tables in markdown format.
    /// </summary>
    public List<string> Tables { get; set; } = new();

    /// <summary>
    /// Description of charts found in the image.
    /// </summary>
    public List<ChartDescription> Charts { get; set; } = new();

    /// <summary>
    /// Description of diagrams found in the image.
    /// </summary>
    public List<DiagramDescription> Diagrams { get; set; } = new();

    /// <summary>
    /// Extracted code snippets.
    /// </summary>
    public List<CodeSnippet> CodeSnippets { get; set; } = new();
}

/// <summary>
/// Description of a chart in an image.
/// </summary>
public class ChartDescription
{
    public string ChartType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> DataPoints { get; set; } = new();
    public string? XAxisLabel { get; set; }
    public string? YAxisLabel { get; set; }
}

/// <summary>
/// Description of a diagram in an image.
/// </summary>
public class DiagramDescription
{
    public string DiagramType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Components { get; set; } = new();
    public List<string> Relationships { get; set; } = new();
}

/// <summary>
/// Code snippet extracted from an image.
/// </summary>
public class CodeSnippet
{
    public string Language { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public double Confidence { get; set; }
}
