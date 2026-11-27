using System.Text.Json.Serialization;
using MusicLayer.Application.UseCases.Lyrics;

namespace MusicLayer.Shared.Lyrics;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(LyricsRecord))]
[JsonSerializable(typeof(LyricsRecord[]))]
[JsonSerializable(typeof(LyricsCacheKey))]
internal partial class LyricsJsonContext : JsonSerializerContext;
