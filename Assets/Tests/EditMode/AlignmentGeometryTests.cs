using NUnit.Framework;
using UnityEngine;

namespace ArduinoAR.Tests
{
    /// <summary>
    /// The alignment-diagnostic helper must reproduce the overlay's original marker placement exactly, so the
    /// diagnostics measure the same geometry the normal overlay draws.
    /// </summary>
    public class AlignmentGeometryTests
    {
        [Test]
        public void PinLocal_MatchesTheOriginalMarkerFormula_ForEveryPin()
        {
            foreach (var profile in new[] { SpduinoProfile.Profile, ArduinoUnoProfile.Profile })
            {
                var size = new Vector2(profile.WidthMm / 1000f, profile.HeightMm / 1000f);
                foreach (var pin in profile.Pins)
                {
                    // Formula used by PhysicalBoardOverlay before the helper existed.
                    var original = new Vector3((pin.PositionMm.x / profile.WidthMm - 0.5f) * size.x, 0.003f, (pin.PositionMm.y / profile.HeightMm - 0.5f) * size.y);
                    var helper = BoardGeometry.PinLocal(pin, profile, size, 0.003f);
                    Assert.AreEqual(original.x, helper.x, 1e-7f, $"{profile.Id} {pin.Id} x");
                    Assert.AreEqual(original.y, helper.y, 1e-7f, $"{profile.Id} {pin.Id} y");
                    Assert.AreEqual(original.z, helper.z, 1e-7f, $"{profile.Id} {pin.Id} z");
                }
            }
        }

        [Test]
        public void Corners_AreTheImageCorners_InTlTrBrBlOrder()
        {
            var c = BoardGeometry.CornersLocal(new Vector2(0.065f, 0.05f));
            CollectionAssert.AreEqual(new[] { "TL", "TR", "BR", "BL" }, BoardGeometry.CornerNames);
            Assert.AreEqual(new Vector3(-0.0325f, 0f, 0.025f), c[0]);
            Assert.AreEqual(new Vector3(0.0325f, 0f, 0.025f), c[1]);
            Assert.AreEqual(new Vector3(0.0325f, 0f, -0.025f), c[2]);
            Assert.AreEqual(new Vector3(-0.0325f, 0f, -0.025f), c[3]);
        }

        [Test]
        public void DemoScene_HasDiagnosticsOffByDefault_ButStillWired()
        {
            string scene = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Scenes/SampleScene.unity"));
            // ARDiagnosticsOverlay component (script guid in its .meta): disabled, with the Diag button's reference kept.
            var overlay = System.Text.RegularExpressions.Regex.Match(scene,
                @"--- !u!114 &(\d+)\nMonoBehaviour:(?:\n  [^\n]*){0,6}?\n  m_Enabled: (\d)\n  m_EditorHideFlags: 0\n  m_Script: \{fileID: 11500000, guid: b6285dcd44ddf13439c520378c602c2f");
            Assert.IsTrue(overlay.Success, "overlay component not found in the scene");
            Assert.AreEqual("0", overlay.Groups[2].Value, "the on-screen AR diagnostics must start disabled");
            StringAssert.Contains($"m_Diagnostics: {{fileID: {overlay.Groups[1].Value}}}", scene, "the Diag button must still be able to enable it");
            StringAssert.DoesNotContain("m_DiagnosticMode: 1", scene, "alignment diagnostics must start off");
        }

        [Test]
        public void SpduinoPins_LieInsideTheBoard()
        {
            var p = SpduinoProfile.Profile;
            var size = new Vector2(0.065f, 0.05f);
            foreach (var pin in p.Pins)
            {
                var l = BoardGeometry.PinLocal(pin, p, size, 0f);
                Assert.That(Mathf.Abs(l.x) <= size.x / 2f && Mathf.Abs(l.z) <= size.y / 2f, pin.Id);
            }
        }
    }
}
