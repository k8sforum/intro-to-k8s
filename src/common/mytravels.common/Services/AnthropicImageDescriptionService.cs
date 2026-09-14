using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Configuration;
using mytravels.contract.CustomException;
using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;

namespace mytravels.common.Services
{
    public class AnthropicImageDescriptionService : IImageDescriptionService
    {
        private const string DefaultModel = "claude-haiku-4-5";
        private const string UnsetAnthropicApiKey = "<YOUR_ANTHROPIC_API_KEY>";
        private const string SystemPrompt =
            "You are a careful, factual travel-photo captioning assistant. Describe only what is directly " +
            "visible in the photo. Never guess or infer information that isn't visually evident - including " +
            "country, city, region, or other place names - unless it is confirmed by legible text within the " +
            "image itself (e.g. a sign, plaque, or license plate). When you are not highly confident about a " +
            "detail, omit it rather than guess. Respond only with the requested JSON.";
        private const string UserPrompt =
            "Describe this photo in one short, factual sentence (max ~20 words), covering only what is " +
            "visibly present. Then give 3-6 single-word lowercase scene tags for concrete visible elements " +
            "(e.g. \"nature\", \"road\", \"ocean\", \"car\"). Include a tag only if you are highly confident " +
            "it applies - prefer fewer, certain tags over more, speculative ones. Do not include a tag naming " +
            "a country, region, or place unless legible text in the photo confirms it.";

        private readonly AnthropicClient _client;
        private readonly bool _configured;
        private readonly string _model;

        public AnthropicImageDescriptionService(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            string apiKey = configuration.GetValue<string>("AnthropicApiKey");
            _configured = !string.IsNullOrEmpty(apiKey) && apiKey != UnsetAnthropicApiKey;
            _client = new AnthropicClient { ApiKey = apiKey };
            string model = configuration.GetValue<string>("AnthropicModel");
            _model = string.IsNullOrEmpty(model) ? DefaultModel : model;
        }

        public async Task<ImageDescriptionDto> DescribeAsync(string base64Image, CancellationToken cancellationToken)
        {
            if (!_configured)
                throw new ApiException(503, "The AI photo description service is not configured (AnthropicApiKey is unset).");

            const long maxBase64SizeBytes = 5 * 1024 * 1024;
            if (base64Image.Length > maxBase64SizeBytes)
                throw new ApiException(413, $"Image is too large for AI processing ({base64Image.Length / (1024 * 1024)}MB). Maximum is 5MB.");

            Dictionary<string, JsonElement> schema = new()
            {
                ["type"] = JsonSerializer.SerializeToElement("object"),
                ["properties"] = JsonSerializer.SerializeToElement(new
                {
                    description = new { type = "string" },
                    tags = new { type = "array", items = new { type = "string" } }
                }),
                ["required"] = JsonSerializer.SerializeToElement(new[] { "description", "tags" }),
                ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
            };

            MessageCreateParams parameters = new()
            {
                Model = _model,
                MaxTokens = 512,
                System = SystemPrompt,
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = new List<ContentBlockParam>
                        {
                            new ImageBlockParam
                            {
                                Source = new Base64ImageSource
                                {
                                    MediaType = "image/jpeg",
                                    Data = base64Image
                                }
                            },
                            new TextBlockParam { Text = UserPrompt }
                        }
                    }
                ],
                OutputConfig = new OutputConfig
                {
                    Format = new JsonOutputFormat { Schema = schema }
                }
            };

            Message response;
            try
            {
                response = await _client.Messages.Create(parameters, cancellationToken: cancellationToken);
            }
            catch (AnthropicRateLimitException ex)
            {
                throw new ApiException(429, $"AI description service is rate-limited: {ex.Message}");
            }
            catch (Anthropic5xxException ex)
            {
                throw new ApiException(502, $"AI description service is unavailable: {ex.Message}");
            }
            catch (AnthropicApiException ex)
            {
                throw new ApiException(502, $"AI description request failed: {ex.Message}");
            }

            if (response.StopReason == "refusal")
                throw new ApiException(422, "The photo could not be analyzed.");

            string text = response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text
                ?? throw new ApiException(502, "AI description service returned no content.");

            try
            {
                return JsonSerializer.Deserialize<ImageDescriptionDto>(
                    text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new ApiException(502, "AI description response could not be parsed.");
            }
            catch (JsonException)
            {
                throw new ApiException(502, "AI description response was not valid JSON.");
            }
        }
    }
}
