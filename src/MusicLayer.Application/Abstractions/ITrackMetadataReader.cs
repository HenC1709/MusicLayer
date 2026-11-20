using MusicLayer.Domain.Entities;

namespace MusicLayer.Application.Abstractions;

public interface ITrackMetadataReader
{
    TrackMetadata? Read(string filePath);
}
