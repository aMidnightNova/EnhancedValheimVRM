using System;
using EnhancedValheimVRM;
using Unity.Mathematics;

internal static class SeatSupportChecks
{
    internal static void Run(Action<bool, string> check)
    {
        void Near(float3 actual, float3 expected, string message)
        {
            check(math.distance(actual, expected) < 0.00002f, message + ": " + actual + " expected " + expected);
        }

        var empty = Create(1, 1);
        check(!empty.TryMeasure(out var missing) && math.all(missing == 0),
            "Missing pelvis geometry must allow hip-only fallback");
        for (var i = 0; i < 7; i++)
        {
            empty.Add(new float3(0, 0.8f, -0.12f),
                new int4(SeatSupportMeasurement.Hips, 0, 0, 0),
                new float4(1, 0, 0, 0));
        }

        check(!empty.TryMeasure(out _), "Too few pelvis vertices must not produce a shape estimate");

        var baseline = Populate(1, 0.4f, 0.1f, 0.06f);
        check(baseline.TryMeasure(out var vanilla), "Seated anatomy measurement failed");
        Near(vanilla,
            new float3(1, 0.2f, 0.06f),
            "Measure ground-to-hip, hip-to-thigh underside and lower leg thickness independently");
        var longerLegs = Populate(1, 0.9f, 0.1f, 0.06f);
        check(longerLegs.TryMeasure(out var longSupport), "Long-thigh measurement failed");
        Near(longSupport, vanilla, "Standing thigh length must not inflate bone-to-mesh thickness");

        var fullerThigh = Populate(1, 0.4f, 0.2f, 0.06f);
        check(fullerThigh.TryMeasure(out var avatar), "Fuller thigh measurement failed");
        Near(avatar, new float3(1, 0.3f, 0.06f), "Thigh thickness is measured from its bone");
        var fullerCalf = Populate(1, 0.4f, 0.1f, 0.09f);
        check(fullerCalf.TryMeasure(out var calfSupport), "Fuller lower leg measurement failed");
        Near(calfSupport,
            new float3(1, 0.2f, 0.09f),
            "Lower leg thickness is measured from its bone over the upper half only");
        var lowHipBone = Populate(1, 0.4f, 0.1f, 0.06f, 0.95f);
        check(lowHipBone.TryMeasure(out var lowHip), "Low hip bone measurement failed");
        Near(lowHip,
            new float3(0.95f, 0.15f, 0.06f),
            "Hip bone height above the thigh joints must count toward seat depth");
        check(!Populate(1, 0.4f, 0.1f, 0.06f, 1, false).TryMeasure(out _),
            "Missing lower leg mesh must fall back like missing thigh mesh");
        foreach (var scale in new[] { 0.5f, 2f })
        {
            var scaled = Populate(scale, 0.4f, 0.1f, 0.06f);
            check(scaled.TryMeasure(out var support), "Scaled mesh measurement failed");
            Near(support, vanilla * scale, "Mesh support must already include avatar scale once");
        }

        // tail and spine verts must not count
        for (var i = 0; i < 500; i++)
        {
            baseline.Add(new float3(0, -10, -10),
                new int4(0, SeatSupportMeasurement.Hips, 0, 0),
                new float4(0.8f, 0.2f, 0, 0));
        }

        // a NaN vert, a pelvis weighted tail, and lone thigh, shin and foot strays
        baseline.Add(new float3(float.NaN, 0, 0), new int4(1, 0, 0, 0), new float4(1, 0, 0, 0));
        baseline.Add(new float3(0, -10, -10), new int4(1, 0, 0, 0), new float4(1, 0, 0, 0));
        baseline.Add(new float3(-0.1f, 0.5f, -10),
            new int4(SeatSupportMeasurement.LeftThigh, 0, 0, 0),
            new float4(1, 0, 0, 0));
        baseline.Add(new float3(-0.1f, 0.45f, -10),
            new int4(SeatSupportMeasurement.LeftShin, 0, 0, 0),
            new float4(1, 0, 0, 0));
        baseline.Add(new float3(0, -10, 0),
            new int4(SeatSupportMeasurement.Foot, 0, 0, 0),
            new float4(1, 0, 0, 0));
        check(baseline.TryMeasure(out var filtered), "Filtered mesh measurement failed");
        Near(filtered, vanilla, "Unrelated bones and isolated tips must not distort seat support");

        // height from hips and thigh thickness, depth from knees and calf thickness
        var vanillaMetrics = new float3(1, 0.1f, 0.05f);
        var avatarMetrics = new float3(0.6f, 0.16f, 0.07f);
        var proportions = AttachmentMath.GetSeatProportions(vanillaMetrics, avatarMetrics);
        Near(proportions, new float3(1, 0.06f, 0.02f), "Valid flag, hip-to-seat delta and lower leg thickness delta");
        Near(AttachmentMath.GetSeatProportions(vanillaMetrics, float3.zero),
            float3.zero,
            "Missing measurements must select the previous alignment fallback");
        Near(AttachmentMath.GetSeatProportions(vanillaMetrics, new float3(float.NaN, 0.1f, 0.04f)),
            float3.zero,
            "Invalid measurements must not reach seated transforms");

        // measured fix plus manual nudge on a turned and tilted seat
        var rotation = quaternion.EulerXYZ(0.1f, 1.2f, -0.2f);
        var origin = new float3(4, 2, -3);
        var vanillaHip = origin + math.rotate(rotation, new float3(0, 0.8f, 0.1f));
        var vanillaKnee = origin + math.rotate(rotation, new float3(0, 0.75f, 0.55f));
        var avatarHip = origin + math.rotate(rotation, new float3(0.03f, 0.48f, 0.06f));
        // knee 0.11 too far forward, calf 0.02 thicker, hips go back 0.09
        var avatarKnee = origin + math.rotate(rotation, new float3(0.03f, 0.45f, 0.66f));
        var correction = AttachmentMath.SeatedProportionCorrection(vanillaHip,
            avatarHip,
            vanillaKnee,
            avatarKnee,
            origin,
            rotation,
            proportions,
            false);
        Near(avatarHip + correction,
            origin + math.rotate(rotation, new float3(0.03f, 0.86f, -0.03f)),
            "Placement must preserve lateral pose, reach the seat height and line up the back of the lower leg");
        var shortKnee = origin + math.rotate(rotation, new float3(0.03f, 0.45f, 0.36f));
        Near(avatarHip + AttachmentMath.SeatedProportionCorrection(vanillaHip,
                avatarHip,
                vanillaKnee,
                shortKnee,
                origin,
                rotation,
                proportions,
                false),
            origin + math.rotate(rotation, new float3(0.03f, 0.86f, 0.27f)),
            "A knee short of the game model's knee must slide the hips forward to it");
        Near(avatarHip + AttachmentMath.SeatedProportionCorrection(vanillaHip,
                avatarHip,
                vanillaKnee,
                avatarKnee,
                origin,
                rotation,
                proportions,
                true),
            origin + math.rotate(rotation, new float3(0.03f, 0.86f, 0.06f)),
            "An avatar taller than the game model must never slide back");
        Near(avatarHip + AttachmentMath.SeatedProportionCorrection(vanillaHip,
                avatarHip,
                vanillaKnee,
                shortKnee,
                origin,
                rotation,
                proportions,
                true),
            origin + math.rotate(rotation, new float3(0.03f, 0.86f, 0.27f)),
            "An avatar taller than the game model still slides forward");
        var lowerAvatarHip = origin + math.rotate(rotation, new float3(0.03f, 0.2f, 0.06f));
        Near(lowerAvatarHip + AttachmentMath.SeatedProportionCorrection(vanillaHip,
                lowerAvatarHip,
                vanillaKnee,
                avatarKnee,
                origin,
                rotation,
                proportions,
                false),
            origin + math.rotate(rotation, new float3(0.03f, 0.86f, -0.03f)),
            "An avatar retargeted lower than its standing ratio predicts must still reach the seat");
        Near(AttachmentMath.SeatedProportionCorrection(vanillaHip,
                avatarHip,
                vanillaKnee,
                avatarKnee,
                vanillaHip,
                rotation,
                proportions,
                false),
            correction,
            "A chair origin at the vanilla hip must not cancel the vertical correction");
        var relocated = new float3(30, 100, -20);
        Near(AttachmentMath.SeatedProportionCorrection(vanillaHip + relocated,
                avatarHip + relocated,
                vanillaKnee + relocated,
                avatarKnee + relocated,
                origin + relocated,
                rotation,
                proportions,
                false),
            correction,
            "World position and elevation must not change seating correction");
        var manual = new float3(0, 0.05f, 0.1f);
        Near(AttachmentMath.PlaceBone(avatarHip + correction, rotation, manual * 0.5f),
            origin + math.rotate(rotation, new float3(0.03f, 0.885f, 0.02f)),
            "Manual tuning must add once after anatomical placement");
        Near(AttachmentMath.SeatedProportionCorrection(vanillaHip,
                avatarHip,
                vanillaKnee,
                avatarKnee,
                origin,
                rotation,
                float3.zero,
                false),
            AttachmentMath.SeatedHipCorrection(vanillaHip, avatarHip, rotation),
            "Missing anatomy fallback");

        foreach (var scale in new[] { 0.5f, 1f, 2f })
        {
            var scaledMetrics = vanillaMetrics * scale;
            var scaledHip = origin + math.rotate(rotation, new float3(0, 0.8f, 0.1f) * scale);
            var scaledKnee = origin + math.rotate(rotation, new float3(0, 0.75f, 0.55f) * scale);
            var placed = scaledHip + AttachmentMath.SeatedProportionCorrection(vanillaHip,
                scaledHip,
                vanillaKnee,
                scaledKnee,
                origin,
                rotation,
                AttachmentMath.GetSeatProportions(vanillaMetrics, scaledMetrics),
                false);
            var local = math.rotate(math.inverse(rotation), placed - origin);
            // back of the calf = knee minus calf thickness
            var legBack = local.z - 0.1f * scale + 0.55f * scale - scaledMetrics.z;
            Near(new float3(0, local.y - scaledMetrics.y, legBack),
                new float3(0, 0.7f, 0.5f),
                "Scaled avatars must keep the vanilla seat underside and back of the lower leg position");
        }
    }

    private static SeatSupportMeasurement Create(float scale, float hipHeight)
    {
        var bend = quaternion.RotateX(-math.PI / 2);
        return new SeatSupportMeasurement(new float3(0, hipHeight, 0) * scale,
            new float3(-0.1f, 0.9f, 0) * scale,
            new float3(0.1f, 0.9f, 0) * scale,
            bend,
            bend,
            new float3(-0.1f, 0.5f, 0) * scale,
            new float3(0.1f, 0.5f, 0) * scale,
            bend,
            bend,
            0.4f * scale,
            0.4f * scale);
    }

    private static SeatSupportMeasurement Populate(float scale,
        float legLength,
        float thighRadius,
        float calfRadius,
        float hipHeight = 1,
        bool shins = true)
    {
        var sample = Create(scale, hipHeight);
        for (var i = 0; i < 100; i++)
        {
            var x = (i - 50) * 0.001f;
            sample.Add(new float3(x, 0, 0.05f) * scale,
                new int4(SeatSupportMeasurement.Foot, 0, 0, 0),
                new float4(1, 0, 0, 0));
            var along = 0.4f * (i + 1) / 100;
            // calf bulge is lower down, only the top half counts
            var calf = along <= 0.2f ? calfRadius : calfRadius + 0.05f;
            foreach (var side in new[] { -1, 1 })
            {
                sample.Add(new float3(side * 0.1f, 0.9f - legLength * (i + 1) / 100, -thighRadius) * scale,
                    new int4(side < 0 ? SeatSupportMeasurement.LeftThigh : SeatSupportMeasurement.RightThigh, 0, 0, 0),
                    new float4(1, 0, 0, 0));
                if (shins)
                {
                    sample.Add(new float3(side * 0.1f, 0.5f - along, -calf) * scale,
                        new int4(side < 0 ? SeatSupportMeasurement.LeftShin : SeatSupportMeasurement.RightShin,
                            0,
                            0,
                            0),
                        new float4(1, 0, 0, 0));
                }
            }
        }

        return sample;
    }
}
