using System;
using EnhancedValheimVRM;
using Unity.Mathematics;

internal static class Program
{
    private static int _checks;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }

    private static void Near(float3 actual, float3 expected, string message)
    {
        Check(math.distance(actual, expected) < 0.00002f, message + ": " + actual + " expected " + expected);
    }

    private static float3 ColumnLengths(float4x4 matrix)
    {
        return new float3(math.length(matrix.c0.xyz), math.length(matrix.c1.xyz), math.length(matrix.c2.xyz));
    }

    private static void Main()
    {
        SettingsChecks.Run(Check);
        var identity = quaternion.identity;
        var turn = quaternion.RotateY(math.PI / 2);
        var gripRotation = quaternion.RotateZ(math.PI / 2);

        // A socket 2 cm along the old bone's Y axis, with socket X pointing along Y.
        var captured = AttachmentMath.ToSocketOffset(new float3(5, 7, 9), new float3(5, 7.02f, 9), gripRotation);
        Near(captured, new float3(0.02f, 0, 0), "Socket-relative offset");
        var parent = float4x4.TRS(new float3(30, 40, 50), turn, new float3(10));
        Check(AttachmentMath.TryMapSocket(parent, turn, identity, captured, 2, out var position),
            "Socket mapping failed");
        Near(position, new float3(0.004f, 0, 0), "Parent-scale conversion");
        Near(math.transform(parent, position), new float3(30, 40, 49.96f), "Rotated grip target in world space");

        // Import-unit changes must not change the intended world-space grip distance.
        foreach (var importScale in new[] { 0.01f, 1, 100 })
        foreach (var avatarRatio in new[] { 0.5f, 1, 2 })
        {
            parent = float4x4.TRS(new float3(3, 4, 5), identity, new float3(importScale));
            var offset = new float3(0.03f, -0.04f, 0.07f);
            Check(AttachmentMath.TryMapSocket(parent, identity, identity, offset, avatarRatio, out position),
                "Import-scale mapping failed");
            Near(math.transform(parent, position) - new float3(3, 4, 5),
                offset * avatarRatio,
                "Avatar ratio must apply once");
            for (var repeat = 0; repeat < 3; repeat++)
            {
                Check(AttachmentMath.TryMapSocket(parent, identity, identity, offset, avatarRatio, out var repeated),
                    "Repeated mapping failed");
                Near(repeated, position, "Repeated mapping must use the same captured baseline");
            }
        }

        // Moving/turning the whole character rotates/translates the result with it.
        var worldTurn = quaternion.EulerXYZ(0.4f, 0.8f, -0.2f);
        var bodyPosition = new float3(-7, 8, 12);
        var bodyMatrix = float4x4.TRS(bodyPosition, worldTurn, new float3(1));
        var sourcePosition = new float3(1, 2, 3);
        var sourceSocket = sourcePosition + new float3(0.1f, 0.2f, 0.3f);
        var firstCapture = AttachmentMath.ToSocketOffset(sourcePosition, sourceSocket, gripRotation);
        var rotatedCapture = AttachmentMath.ToSocketOffset(math.transform(bodyMatrix, sourcePosition),
            math.transform(bodyMatrix, sourceSocket),
            math.mul(worldTurn, gripRotation));
        Near(rotatedCapture, firstCapture, "Capture must be independent of facing/position");
        var target = float4x4.TRS(new float3(2, 3, 4), turn, new float3(0.5f));
        Check(AttachmentMath.TryMapSocket(target, turn, gripRotation, firstCapture, 1, out var firstPosition),
            "Base target mapping");
        var movedTarget = math.mul(bodyMatrix, target);
        Check(AttachmentMath.TryMapSocket(movedTarget,
                math.mul(worldTurn, turn),
                gripRotation,
                rotatedCapture,
                1,
                out var movedPosition),
            "Moved target mapping");
        Near(movedPosition, firstPosition, "Socket local position must be invariant under whole-character motion");

        // Equipment size must include rotation under a nonuniformly scaled parent.
        parent = float4x4.Scale(new float3(2, 3, 4));
        Check(AttachmentMath.TryLocalScale(parent, gripRotation, new float3(1), out var localScale),
            "Scale conversion failed");
        Near(localScale, new float3(1f / 3, 0.5f, 0.25f), "Rotated local axes use the correct parent scale");
        Near(ColumnLengths(math.mul(parent, float4x4.TRS(float3.zero, gripRotation, localScale))),
            new float3(1),
            "World equipment scale");

        // Nested nonuniform scales can produce shear: preserve requested axis lengths.
        parent = math.mul(float4x4.Scale(new float3(2, 3, 4)),
            float4x4.TRS(new float3(5), worldTurn, new float3(0.5f, 2, 1)));
        Check(AttachmentMath.TryLocalScale(parent, gripRotation, new float3(0.5f), out localScale),
            "Nested scale conversion failed");
        Near(ColumnLengths(math.mul(parent, float4x4.TRS(float3.zero, gripRotation, localScale))),
            new float3(0.5f),
            "Nested world scale");
        Check(AttachmentMath.TryMapSocket(parent, worldTurn, gripRotation, captured, 1, out position),
            "Nested socket mapping failed");
        Near(math.transform(parent, position) - parent.c3.xyz,
            math.rotate(math.mul(worldTurn, gripRotation), captured),
            "Nested displacement mapping");

        // The desired weapon size remains linear in avatar size, not squared by the VRM root.
        foreach (var ratio in new[] { 0.5f, 1, 2 })
        {
            parent = float4x4.Scale(new float3(ratio));
            Check(AttachmentMath.TryLocalScale(parent, identity, new float3(1), out var socketScale),
                "Socket scale failed");
            var socketMatrix = math.mul(parent, float4x4.Scale(socketScale));
            Check(AttachmentMath.TryLocalScale(socketMatrix, identity, new float3(ratio), out var itemScale),
                "Equipment scale failed");
            Near(ColumnLengths(math.mul(socketMatrix, float4x4.Scale(itemScale))),
                new float3(ratio),
                "Equipment double-scaled");
        }

        // World-derived dual-wield offsets must be converted before self-space application.
        var handPosition = new float3(10, 20, 30);
        var worldOffset = new float3(0.02f, -0.03f, 0.04f);
        var correction = new float3(0, -0.01f, 0.02f);
        var boneOffset = AttachmentMath.ToBoneOffset(turn, worldOffset, correction);
        Near(AttachmentMath.PlaceBone(handPosition, turn, boneOffset),
            handPosition + worldOffset + math.rotate(turn, correction),
            "Dual-wield space conversion");
        var rotatedBoneOffset =
            AttachmentMath.ToBoneOffset(math.mul(worldTurn, turn), math.rotate(worldTurn, worldOffset), correction);
        Near(rotatedBoneOffset, boneOffset, "Dual-wield setup depends on character facing");
        Near(AttachmentMath.PlaceBone(math.rotate(worldTurn, handPosition), math.mul(worldTurn, turn), boneOffset),
            math.rotate(worldTurn, AttachmentMath.PlaceBone(handPosition, turn, boneOffset)),
            "Dual-wield animation rotation");

        // Regression: assigning a 0.13 m back offset directly below a 100x socket
        // places the item 13 m away, even when its mesh scale has been corrected.
        foreach (var socketScale in new[] { 0.01f, 1f, 100f })
        foreach (var avatarRatio in new[] { 0.5f, 0.9043996f, 2f })
        {
            var socketPosition = new float3(20, 50, 4);
            var socketMatrix = float4x4.TRS(socketPosition, worldTurn, new float3(socketScale));
            var configuredOffset = new float3(0, 0.13f, 0);
            Check(AttachmentMath.TryMapItemOffset(socketMatrix,
                    worldTurn,
                    configuredOffset,
                    avatarRatio,
                    out var itemPosition),
                "Back item offset conversion");
            Near(math.transform(socketMatrix, itemPosition),
                socketPosition + math.rotate(worldTurn, configuredOffset * avatarRatio),
                "Back item distance/direction must not depend on import scale");
            Check(AttachmentMath.TryMapItemOffset(socketMatrix,
                    worldTurn,
                    configuredOffset,
                    avatarRatio,
                    out var repeatedPosition),
                "Repeated back offset conversion");
            Near(repeatedPosition, itemPosition, "Holstering must not accumulate offset");
        }

        Check(!AttachmentMath.TryMapItemOffset(float4x4.Scale(float3.zero),
                identity,
                new float3(0, 0.13f, 0),
                1,
                out position),
            "Singular back socket accepted");

        // Settings adjust the prefab's equipoffset rather than erasing it.
        var equipBase = AttachmentMath.ToSocketOffset(new float3(1, 2, 3), new float3(1.03f, 2, 3), identity);
        var equipParent = float4x4.TRS(new float3(4, 5, 6), turn, new float3(100));
        Check(AttachmentMath.TryMapItemOffset(equipParent,
                turn,
                equipBase + new float3(0, 0.13f, 0),
                0.5f,
                out var equipPosition),
            "Prefab offset mapping");
        Near(math.transform(equipParent, equipPosition),
            new float3(4, 5.065f, 5.985f),
            "Prefab and configured offsets must both survive scale conversion");

        // A measured 6-degree physics turn must render as intermediate angles,
        // including frames that do not contain another physics update.
        var smoother = new FixedRotationSmoother();
        var forward = new float3(0, 0, 1);
        var sixDegrees = quaternion.RotateY(math.radians(6));
        Near(math.rotate(smoother.Sample(identity, float3.zero, 0, 0, 0.02f), forward),
            forward,
            "First turn sample must snap");
        Near(math.rotate(smoother.Sample(sixDegrees, float3.zero, 0.02f, 0.02f, 0.02f), forward),
            forward,
            "New fixed tick starts at previous rotation");
        Near(math.rotate(smoother.Sample(sixDegrees, float3.zero, 0.02f, 0.025f, 0.02f), forward),
            math.rotate(quaternion.RotateY(math.radians(1.5f)), forward),
            "Quarter tick interpolation");
        Near(math.rotate(smoother.Sample(sixDegrees, float3.zero, 0.02f, 0.03f, 0.02f), forward),
            math.rotate(quaternion.RotateY(math.radians(3)), forward),
            "Half tick interpolation");
        Near(math.rotate(smoother.Sample(sixDegrees, float3.zero, 0.02f, 0.04f, 0.02f), forward),
            math.rotate(sixDegrees, forward),
            "Completed interpolation");
        Near(math.rotate(smoother.Sample(sixDegrees, float3.zero, 0.04f, 0.06f, 0.02f), forward),
            math.rotate(sixDegrees, forward),
            "Stopped turn must settle without oscillation");

        smoother.Reset();
        smoother.Sample(quaternion.RotateY(math.radians(359)), float3.zero, 0, 0, 0.02f);
        Near(math.rotate(smoother.Sample(quaternion.RotateY(math.radians(1)), float3.zero, 0.02f, 0.03f, 0.02f),
                forward),
            forward,
            "Turn through north must take short path");
        Near(math.rotate(smoother.Sample(turn, new float3(100, 0, 0), 0.04f, 0.045f, 0.02f), forward),
            math.rotate(turn, forward),
            "Teleport must discard old rotation");
        Near(math.rotate(smoother.Sample(identity, new float3(100, 0, 0), 1, 1, 0.02f), forward),
            forward,
            "Long hitch must discard stale samples");
        Near(math.rotate(smoother.Sample(sixDegrees, new float3(100, 0, 0), 0, 0, 0.02f), forward),
            math.rotate(sixDegrees, forward),
            "Clock reset must discard stale samples");
        smoother.Reset();
        Near(math.rotate(smoother.Sample(turn, float3.zero, 0, 0, 0), forward),
            math.rotate(turn, forward),
            "Invalid fixed step must snap");

        // Correcting a rigged weapon's root turn preserves its authored hand axes.
        var handAxes = quaternion.EulerXYZ(0.2f, -0.3f, 0.4f);
        var renderedTurn = quaternion.RotateY(math.radians(3));
        var turnCorrection = math.mul(renderedTurn, math.inverse(sixDegrees));
        Near(math.rotate(math.mul(turnCorrection, math.mul(sixDegrees, handAxes)), forward),
            math.rotate(math.mul(renderedTurn, handAxes), forward),
            "Special weapon must follow rendered root rotation");

        Check(!AttachmentMath.TryMapSocket(float4x4.Scale(new float3(0)),
                identity,
                identity,
                captured,
                1,
                out position),
            "Singular parent accepted");
        Check(!AttachmentMath.TryMapSocket(float4x4.identity, identity, identity, captured, float.NaN, out position),
            "NaN ratio accepted");
        Check(!AttachmentMath.TryMapSocket(float4x4.identity, identity, identity, captured, 0, out position),
            "Zero ratio accepted");
        Check(!AttachmentMath.TryLocalScale(float4x4.Scale(new float3(0)), identity, new float3(1), out localScale),
            "Zero parent scale accepted");
        Console.WriteLine("PASS: " + _checks + " attachment math and equipment identity checks.");
    }
}
