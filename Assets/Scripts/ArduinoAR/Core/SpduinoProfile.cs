using System.Collections.Generic;
using UnityEngine;

namespace ArduinoAR
{
    /// <summary>
    /// Knowledge profile for the red SPDUINO / SP ROBOT WORKS board in the reference photo
    /// (Assets/ReferenceImages/SPDUINO_Compatible_Board.jpg).
    ///
    /// What the photo shows: FTDI FT232RL USB-serial chip, 16 MHz crystal, 28-pin DIP ATmega
    /// (part number only partly legible), ICSP header, and these header labels:
    ///   top:    AREF GND 13 12 ~11 ~10 ~9 8 | 7 ~6 ~5 4 ~3 2 TX1 RX0
    ///   bottom: RST 3.3V 5V GND GND VIN | A0 A1 A2 A3 A4 A5
    /// There are no SDA/SCL or IOREF header pins (older Duemilanove-style layout).
    ///
    /// Pin labels and PWM (~) marks are read from the silkscreen. Everything else
    /// (I2C on A4/A5, SPI on 10-13, interrupts on 2/3, LED on 13) is inferred from the
    /// ATmega328P/Arduino-compatible design and is marked unverified until confirmed.
    /// Pin positions are measured header-hole centres in the perspective-corrected reference image
    /// (WorkInProgress/SPDUINO_Rectification, 18 px/mm), in mm from the PCB bottom-left corner, y up.
    /// Accuracy is about 1 mm. Header tops sit above the PCB, so markers drawn on the board plane can
    /// show parallax at oblique views (header height not yet measured).
    /// </summary>
    public static class SpduinoProfile
    {
        public const string Id = "spduino_atmega_compatible";
        public const float WidthMm = 65f;   // measured by the user
        public const float HeightMm = 50f;  // measured by the user

        // Header-hole centres (mm, origin PCB bottom-left, y up) measured in the rectified
        // 65 x 50 mm reference image; source: spduino_pins_mm_rectified.json.
        static readonly Dictionary<string, Vector2> k_PinMm = new()
        {
            { "AREF", new Vector2(23.33f, 49.93f) },
            { "GND_TOP", new Vector2(25.92f, 49.88f) },
            { "D13", new Vector2(28.46f, 49.89f) },
            { "D12", new Vector2(31.06f, 49.90f) },
            { "D11", new Vector2(33.66f, 49.90f) },
            { "D10", new Vector2(36.38f, 49.92f) },
            { "D9", new Vector2(38.82f, 49.80f) },
            { "D8", new Vector2(41.43f, 49.81f) },
            { "D7", new Vector2(45.51f, 49.83f) },
            { "D6", new Vector2(48.13f, 49.78f) },
            { "D5", new Vector2(50.74f, 49.85f) },
            { "D4", new Vector2(53.20f, 49.73f) },
            { "D3", new Vector2(55.76f, 49.68f) },
            { "D2", new Vector2(58.33f, 49.63f) },
            { "D1", new Vector2(60.79f, 49.63f) },
            { "D0", new Vector2(63.36f, 49.57f) },
            { "RESET", new Vector2(32.84f, 0.59f) },
            { "3V3", new Vector2(35.45f, 0.74f) },
            { "5V", new Vector2(37.90f, 0.84f) },
            { "GND1", new Vector2(40.60f, 0.60f) },
            { "GND2", new Vector2(43.12f, 0.58f) },
            { "VIN", new Vector2(45.58f, 0.62f) },
            { "A0", new Vector2(50.63f, 1.65f) },
            { "A1", new Vector2(53.27f, 1.69f) },
            { "A2", new Vector2(55.80f, 1.73f) },
            { "A3", new Vector2(58.27f, 1.76f) },
            { "A4", new Vector2(60.75f, 1.80f) },
            { "A5", new Vector2(63.34f, 1.90f) },
        };

        const string k_Inferred = "Inferred from the ATmega328P-compatible design; not confirmed on this board (MCU marking partly unreadable).";
        const string k_Silkscreen = "Label read from the board silkscreen.";
        // The listing does not name a board revision; its photo shows a "V2.1" board.
        const string k_ProductPage = "SP Robotic Works SPDUINO product page (retrieved 29 Oct 2026; board revision not stated)";

        static BoardProfile s_Profile;
        public static BoardProfile Profile => s_Profile ??= new BoardProfile
        {
            Id = Id,
            DisplayName = "SPDUINO (SP Robot Works) - Arduino-compatible",
            ShortName = "SPDUINO",
            WidthMm = WidthMm,
            HeightMm = HeightMm,
            Pins = Build(),
            FullyVerified = false,
            VerificationNote = "SPDUINO pin labels come from the board photo; functions beyond the printed labels are assumed ATmega328P-compatible and need verification.",
            ConfirmedStatus = VerificationStatus.VerifiedOnBoard,
            ConfirmedSource = "SPDUINO silkscreen label (reference photo)",
            InferredSource = "Inferred from the ATmega328P-compatible design (MCU marking partly unreadable)",
            BoardFacts = new[]
            {
                new BoardFact("USB-to-serial is an FTDI FT232RL chip.", VerificationStatus.VerifiedOnBoard, "Component marking in the reference photo", FactTags.Usb),
                new BoardFact("A 16 MHz crystal is fitted (marking 16.000).", VerificationStatus.VerifiedOnBoard, "Crystal marking in the reference photo", FactTags.Clock),
                new BoardFact("The microcontroller is a 28-pin DIP ATmega; its exact part number is only partly legible, so ATmega328P is assumed, not confirmed.", VerificationStatus.Inferred, "MCU marking partly legible in the reference photo", FactTags.Mcu),
                new BoardFact("The manufacturer lists the microcontroller as an ATmega328 with the Uno bootloader.", VerificationStatus.Documented, k_ProductPage, FactTags.Mcu),
                new BoardFact("Memory (manufacturer listing for the ATmega328): 32 KB flash, of which 2 KB is used by the bootloader; 2 KB SRAM; 1 KB EEPROM.", VerificationStatus.Documented, k_ProductPage, FactTags.Memory),
                new BoardFact("Operating (logic) voltage: 5 V.", VerificationStatus.Documented, k_ProductPage, FactTags.Voltage),
                new BoardFact("14 digital I/O pins (6 with PWM) and 6 analog inputs.", VerificationStatus.Documented, k_ProductPage, FactTags.Pins),
                new BoardFact("The headers have no separate SDA/SCL or IOREF pins (older Duemilanove-style layout).", VerificationStatus.VerifiedOnBoard, "Header labels in the reference photo", FactTags.Headers),
                new BoardFact("Connectors and controls on the board: USB Type-B socket, DC power jack, 2-pin VIN/PWR connector, 6-pin ICSP header and a reset button.", VerificationStatus.VerifiedOnBoard, "Components visible in the reference photo", FactTags.Connectors),
                new BoardFact("The board measures about 65 x 50 mm.", VerificationStatus.VerifiedOnBoard, "Measured by the user with the physical board", FactTags.Size),
            },
        };

        static Vector2 Mm(string id) => k_PinMm[id];

        static PinRecord Pin(string id, string label, PinHeader header, Vector2 pos, string description, string note, string[] caps, params string[] inferred) =>
            new()
            {
                Id = id, Label = label, Header = header, PositionMm = pos, Description = description,
                Verified = inferred.Length == 0, VerificationNote = note,
                Capabilities = new HashSet<string>(caps), InferredCapabilities = new HashSet<string>(inferred),
            };

        static List<PinRecord> Build()
        {
            var pins = new List<PinRecord>();

            // Top-left header (photo x/y of each hole): AREF GND 13 12 11 10 9 8
            pins.Add(Pin("AREF", "AREF", PinHeader.DigitalTop, Mm("AREF"), "Analog reference input.", k_Silkscreen, new[] { PinCapability.AnalogReference }));
            pins.Add(Pin("GND_TOP", "GND", PinHeader.DigitalTop, Mm("GND_TOP"), "Ground (0 V).", k_Silkscreen, new[] { PinCapability.Ground }));
            pins.Add(Digital(13, Mm("D13")));
            pins.Add(Digital(12, Mm("D12")));
            pins.Add(Digital(11, Mm("D11")));
            pins.Add(Digital(10, Mm("D10")));
            pins.Add(Digital(9, Mm("D9")));
            pins.Add(Digital(8, Mm("D8")));

            // Top-right header: 7 6 5 4 3 2 TX1 RX0
            for (int d = 7; d >= 0; d--) pins.Add(Digital(d, Mm($"D{d}")));

            // Bottom power header: RST 3.3V 5V GND GND VIN
            pins.Add(Pin("RESET", "RST", PinHeader.PowerBottom, Mm("RESET"), "Pull LOW to reset the microcontroller.", k_Silkscreen, new[] { PinCapability.Reset }));
            pins.Add(Pin("3V3", "3.3V", PinHeader.PowerBottom, Mm("3V3"), "3.3 V output (label on silkscreen). Its source and current limit are not confirmed for this board; check before loading it.", "3.3 V label confirmed; source and current limit not confirmed.", new[] { PinCapability.Power }));
            pins.Add(Pin("5V", "5V", PinHeader.PowerBottom, Mm("5V"), "5 V supply output.", k_Silkscreen, new[] { PinCapability.Power }));
            pins.Add(Pin("GND1", "GND", PinHeader.PowerBottom, Mm("GND1"), "Ground (0 V).", k_Silkscreen, new[] { PinCapability.Ground }));
            pins.Add(Pin("GND2", "GND", PinHeader.PowerBottom, Mm("GND2"), "Ground (0 V).", k_Silkscreen, new[] { PinCapability.Ground }));
            pins.Add(Pin("VIN", "VIN", PinHeader.PowerBottom, Mm("VIN"), "Raw input voltage from the DC jack / VIN connector.", k_Silkscreen, new[] { PinCapability.Power }));

            // Bottom analog header: A0..A5
            for (int a = 0; a <= 5; a++)
            {
                var caps = new List<string> { PinCapability.AnalogInput, PinCapability.DigitalAlt };
                var inferred = new List<string> { PinCapability.DigitalAlt };
                string desc = $"Analog input A{a} (label on silkscreen). On ATmega328P also digital pin D{14 + a}.";
                if (a == 4) { caps.Add(PinCapability.I2cSda); inferred.Add(PinCapability.I2cSda); desc += " I2C SDA on ATmega328P (unverified here)."; }
                if (a == 5) { caps.Add(PinCapability.I2cScl); inferred.Add(PinCapability.I2cScl); desc += " I2C SCL on ATmega328P (unverified here)."; }
                pins.Add(Pin($"A{a}", $"A{a}", PinHeader.AnalogBottom, Mm($"A{a}"), desc, a >= 4 ? k_Inferred : k_Silkscreen, caps.ToArray(), inferred.ToArray()));
            }
            return pins;
        }

        static PinRecord Digital(int d, Vector2 pos)
        {
            // Digital use, pin numbers, TX1/RX0 and the PWM (~) marks are printed on the silkscreen.
            // Interrupt, SPI and LED roles are ATmega328P/Arduino conventions: inferred, not confirmed.
            var caps = new List<string> { PinCapability.Digital };
            var inferred = new List<string>();
            string desc = "Digital I/O.";
            switch (d)
            {
                case 0: caps.Add(PinCapability.SerialRx); desc += " Serial RX (silkscreen RX0), shared with the FTDI USB link."; break;
                case 1: caps.Add(PinCapability.SerialTx); desc += " Serial TX (silkscreen TX1), shared with the FTDI USB link."; break;
                case 2: caps.Add(PinCapability.Interrupt); inferred.Add(PinCapability.Interrupt); desc += " External interrupt INT0 (ATmega328P; unverified)."; break;
                case 3: caps.Add(PinCapability.Pwm); caps.Add(PinCapability.Interrupt); inferred.Add(PinCapability.Interrupt); desc += " PWM (~ on silkscreen); INT1 on ATmega328P (unverified)."; break;
                case 5: case 6: case 9: caps.Add(PinCapability.Pwm); desc += " PWM (~ on silkscreen)."; break;
                case 10: caps.Add(PinCapability.Pwm); caps.Add(PinCapability.SpiSS); inferred.Add(PinCapability.SpiSS); desc += " PWM (~ on silkscreen); SPI SS on ATmega328P (unverified)."; break;
                case 11: caps.Add(PinCapability.Pwm); caps.Add(PinCapability.SpiMosi); inferred.Add(PinCapability.SpiMosi); desc += " PWM (~ on silkscreen); SPI MOSI on ATmega328P (unverified)."; break;
                case 12: caps.Add(PinCapability.SpiMiso); inferred.Add(PinCapability.SpiMiso); desc += " SPI MISO on ATmega328P (unverified)."; break;
                case 13: caps.Add(PinCapability.SpiSck); caps.Add(PinCapability.BuiltinLed); inferred.Add(PinCapability.SpiSck); inferred.Add(PinCapability.BuiltinLed); desc += " SPI SCK on ATmega328P; on-board LED not confirmed on this board."; break;
            }
            string label = d == 0 ? "RX0" : d == 1 ? "TX1" : caps.Contains(PinCapability.Pwm) ? $"~{d}" : d.ToString();
            var pin = Pin($"D{d}", label, PinHeader.DigitalTop, pos, desc, inferred.Count > 0 ? k_Inferred : k_Silkscreen, caps.ToArray(), inferred.ToArray());
            if (d == 13) pin.LookupAnswer = "D13 on the SPDUINO is a digital I/O pin; its number is printed on the board. On ATmega328 boards it is usually also the SPI clock (SCK) and the on-board LED pin, but both are UNVERIFIED on this board.";
            return pin;
        }
    }
}
