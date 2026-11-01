using System.Collections.Generic;

namespace ArduinoAR
{
    /// <summary>How well a knowledge fact is established.</summary>
    public enum VerificationStatus
    {
        /// <summary>Confirmed on this physical board (e.g. silkscreen label or component marking in the reference photo).</summary>
        VerifiedOnBoard,
        /// <summary>From the board's published reference documentation.</summary>
        Documented,
        /// <summary>Assumed from the MCU or a reference design; not confirmed on this board.</summary>
        Inferred,
        /// <summary>General electronics / Arduino concept knowledge, not specific to one board.</summary>
        General,
    }

    /// <summary>A knowledge profile for one development board: its pins plus how far the data is verified.</summary>
    public sealed class BoardProfile
    {
        public string Id;
        public string DisplayName;
        string m_ShortName;
        /// <summary>Short name for comparisons and wiring steps ("SPDUINO", "Uno"); defaults to the display name.</summary>
        public string ShortName { get => m_ShortName ?? DisplayName; set => m_ShortName = value; }
        public float WidthMm;
        public float HeightMm;
        public IReadOnlyList<PinRecord> Pins;
        /// <summary>False while any pin capability is inferred rather than confirmed for this exact board.</summary>
        public bool FullyVerified;
        public string VerificationNote;
        /// <summary>Status and source for capabilities that are not in <see cref="PinRecord.InferredCapabilities"/>.</summary>
        public VerificationStatus ConfirmedStatus = VerificationStatus.Documented;
        public string ConfirmedSource;
        /// <summary>Source cited for inferred capabilities.</summary>
        public string InferredSource;
        /// <summary>Board-level facts (MCU, USB chip, layout), each with its own status and evidence.</summary>
        public IReadOnlyList<BoardFact> BoardFacts = new BoardFact[0];
    }

    /// <summary>A board-level statement with its own verification status and source.</summary>
    public readonly struct BoardFact
    {
        public readonly string Text;
        public readonly VerificationStatus Status;
        public readonly string Source;
        /// <summary>Attributes this fact answers (see <see cref="FactTags"/>), used for attribute questions and comparisons.</summary>
        public readonly string[] Tags;

        public BoardFact(string text, VerificationStatus status, string source, params string[] tags)
        {
            Text = text;
            Status = status;
            Source = source;
            Tags = tags ?? new string[0];
        }
    }

    /// <summary>
    /// Where profile points sit on a tracked board image. Local frame of the tracked image: +X = image right,
    /// +Z = image top, +Y = out of the image (towards the camera); metres. The image covers the whole PCB.
    /// </summary>
    public static class BoardGeometry
    {
        /// <summary>Pin position (profile mm from the PCB's bottom-left corner) scaled onto the image, at a height above it.</summary>
        public static UnityEngine.Vector3 PinLocal(PinRecord pin, BoardProfile profile, UnityEngine.Vector2 imageSize, float heightM) =>
            new((pin.PositionMm.x / profile.WidthMm - 0.5f) * imageSize.x, heightM, (pin.PositionMm.y / profile.HeightMm - 0.5f) * imageSize.y);

        public static readonly string[] CornerNames = { "TL", "TR", "BR", "BL" };

        /// <summary>Image (= PCB) corners in <see cref="CornerNames"/> order, in the image plane.</summary>
        public static UnityEngine.Vector3[] CornersLocal(UnityEngine.Vector2 imageSize)
        {
            float hx = imageSize.x * 0.5f, hz = imageSize.y * 0.5f;
            return new[] { new UnityEngine.Vector3(-hx, 0f, hz), new UnityEngine.Vector3(hx, 0f, hz), new UnityEngine.Vector3(hx, 0f, -hz), new UnityEngine.Vector3(-hx, 0f, -hz) };
        }
    }

    /// <summary>Board-fact attribute tags. The order of <see cref="ComparisonOrder"/> is the order of comparison rows.</summary>
    public static class FactTags
    {
        public const string Mcu = "mcu", Usb = "usb", Clock = "clock", Memory = "memory", Voltage = "voltage",
            Pins = "pins", Headers = "headers", Connectors = "connectors", Size = "size";

        public static readonly string[] ComparisonOrder = { Mcu, Usb, Clock, Memory, Voltage, Pins, Headers, Connectors, Size };

        public static string Title(string tag) => tag switch
        {
            Mcu => "Microcontroller",
            Usb => "USB-to-serial",
            Clock => "Clock",
            Memory => "Memory",
            Voltage => "Operating voltage",
            Pins => "I/O pins",
            Headers => "SDA/SCL and IOREF headers",
            Connectors => "Connectors",
            Size => "Size",
            _ => tag,
        };
    }

    /// <summary>Maps recognized reference-image names to board profiles.</summary>
    public static class BoardProfileRegistry
    {
        public const string SpduinoReferenceName = "SPDUINO_Compatible_Board";

        static readonly Dictionary<string, System.Func<BoardProfile>> s_ByReferenceName = new()
        {
            { SpduinoReferenceName, () => SpduinoProfile.Profile },
        };

        /// <summary>Profile for a tracked reference image, or null when the image isn't a known board.</summary>
        public static BoardProfile ForReferenceImage(string referenceName) =>
            referenceName != null && s_ByReferenceName.TryGetValue(referenceName, out var get) ? get() : null;

        /// <summary>Every board the assistant has knowledge for (used for comparisons and questions naming another board).</summary>
        public static IReadOnlyList<BoardProfile> All => new[] { SpduinoProfile.Profile, ArduinoUnoProfile.Profile };

        public static BoardProfile ById(string id)
        {
            foreach (var b in All) if (b.Id == id) return b;
            return null;
        }
    }
}
