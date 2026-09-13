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
            "You are a terse travel-photo captioning assistant. Given a photo, respond only with the requested JSON.";
        private const string UserPrompt =
            "Describe this photo in one short sentence (max ~20 words), then give 3-6 single-word lowercase scene tags (e.g. \"nature\", \"road\", \"ocean\", \"car\").";

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
