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
