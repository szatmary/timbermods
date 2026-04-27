using Timberborn.CameraSystem;
using UnityEngine;

namespace Clockwork.Services;

/// Pans the camera to focus on a world position.
public sealed class BuildingPing
{
    private readonly CameraService _camera;

    public BuildingPing(CameraService camera)
    {
        _camera = camera;
    }

    public void Focus(Vector3 worldPosition) => _camera.MoveTargetTo(worldPosition);
}
