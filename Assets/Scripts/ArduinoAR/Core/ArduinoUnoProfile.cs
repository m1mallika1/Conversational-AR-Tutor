using System.Collections.Generic;
using UnityEngine;

namespace ArduinoAR
{
    /// <summary>Semantic capability tags a pin can have.</summary>
    public static class PinCapability
    {
        public const string Digital = "digital";
        public const string DigitalAlt = "digital_alt";   // analog pins usable as digital D14-D19
        public const string Pwm = "pwm";
        public const string AnalogInput = "analog_input";
        public const string SerialRx = "serial_rx";
        public const string SerialTx = "serial_tx";
        public const string Interrupt = "interrupt";
        public const string SpiSS = "spi_ss";
        public const string SpiMosi = "spi_mosi";
        public const string SpiMiso = "spi_miso";
        public const string SpiSck = "spi_sck";
        public const string I2cSda = "i2c_sda";
        public const string I2cScl = "i2c_scl";
        public const string BuiltinLed = "led_builtin";
        public const string Power = "power";
        public const string Ground = "ground";
        public const string Reset = "reset";
        public const string AnalogReference = "aref";
    }

    public enum PinHeader { DigitalTop, PowerBottom, AnalogBottom }

    /// <summary>One structured record of the semantic profile.</summary>
    public sealed class PinRecord
    {
        public string Id;            // unique key, e.g. "D3", "A0", "GND1"
        public string Label;         // silkscreen label shown on the board
        public PinHeader Header;
        public HashSet<string> Capabilities;
        public string Description;
        public Vector2 PositionMm;   // from the board's bottom-left corner (USB side left)
        /// <summary>False when the capability is inferred (e.g. from the MCU datasheet) rather than confirmed for this board.</summary>
        public bool Verified = true;
        public string VerificationNote;
        /// <summary>Capabilities assumed from the MCU/reference design but not confirmed on this board.</summary>
        public HashSet<string> InferredCapabilities = new();

        public bool Has(string capability) => Capabilities.Contains(capability);
        public bool IsVerified(string capability) => Capabilities.Contains(capability) && !InferredCapabilities.Contains(capability);
    }

    /// <summary>
    /// Structured semantic data profile of the Arduino Uno Rev3 (ATmega328P).
    /// Single source of truth for retrieval, answers and board highlighting.
    /// </summary>
    public static class ArduinoUnoProfile
    {
        public const string BoardName = "Arduino Uno Rev3";
        public const float BoardWidthMm = 68.6f;
        public const float BoardHeightMm = 53.4f;
        const float k_Pitch = 2.54f;
        const string k_Datasheet = "Arduino UNO R3 datasheet / user manual (SKU A000066), retrieved 29 Oct 2026";
        const string k_StorePage = "Arduino UNO R3 tech specs, official Arduino store page, retrieved 29 Oct 2026";

        static List<PinRecord> s_Pins;
        public static IReadOnlyList<PinRecord> Pins => s_Pins ??= Build();

        static BoardProfile s_Profile;
        public static BoardProfile Profile => s_Profile ??= new BoardProfile
        {
            Id = "arduino_uno_r3",
            DisplayName = BoardName,
            ShortName = "Arduino Uno",
            WidthMm = BoardWidthMm,
            HeightMm = BoardHeightMm,
            Pins = Pins,
            FullyVerified = true,
            VerificationNote = "Reference Arduino Uno R3 pinout (virtual demo board).",
            ConfirmedStatus = VerificationStatus.Documented,
            ConfirmedSource = "Arduino Uno Rev3 documented pinout",
            InferredSource = "Arduino Uno Rev3 documented pinout",
            BoardFacts = new[]
            {
                new BoardFact("The Arduino Uno Rev3 uses the ATmega328P microcontroller.", VerificationStatus.Documented, k_Datasheet, FactTags.Mcu),
                new BoardFact("Clock speed: 16 MHz.", VerificationStatus.Documented, k_StorePage, FactTags.Clock),
                new BoardFact("USB-to-serial on the Uno Rev3 is handled by an ATmega16U2.", VerificationStatus.Documented, k_Datasheet, FactTags.Usb),
                new BoardFact("Memory: 32 KB flash, of which 0.5 KB is used by the bootloader; 2 KB SRAM; 1 KB EEPROM.", VerificationStatus.Documented, k_StorePage, FactTags.Memory),
                new BoardFact("Operating voltage: 5 V.", VerificationStatus.Documented, k_StorePage, FactTags.Voltage),
                new BoardFact("14 digital I/O pins (6 with PWM) and 6 analog inputs.", VerificationStatus.Documented, k_Datasheet, FactTags.Pins),
                new BoardFact("The Uno Rev3 repeats the I2C signals (A4/A5) as SDA/SCL pins next to AREF and adds an IOREF pin.", VerificationStatus.Documented, k_Datasheet, FactTags.Headers),
                new BoardFact("Connectors and controls: USB-B connector, 2.1 x 5.5 mm power jack, ICSP header and a reset button.", VerificationStatus.Documented, k_Datasheet, FactTags.Connectors),
                new BoardFact("The board measures 68.6 x 53.4 mm.", VerificationStatus.Documented, k_StorePage, FactTags.Size),
            },
        };

        static PinRecord Pin(string id, string label, PinHeader header, string description, Vector2 pos, params string[] caps) =>
            new() { Id = id, Label = label, Header = header, Description = description, PositionMm = pos, Capabilities = new HashSet<string>(caps) };

        static List<PinRecord> Build()
        {
            var pins = new List<PinRecord>();

            // Top header, right to left: D0..D7, gap, D8..D13, GND, AREF, SDA, SCL.
            const float topY = 50.8f;
            float x = 63.5f;
            for (int d = 0; d <= 13; d++)
            {
                pins.Add(DigitalPin(d, new Vector2(x, topY)));
                x -= k_Pitch;
                if (d == 7) x -= 1.5f; // gap between the two top headers
            }
            pins.Add(Pin("GND_TOP", "GND", PinHeader.DigitalTop, "Ground (0 V) reference.", new Vector2(x, topY), PinCapability.Ground)); x -= k_Pitch;
            pins.Add(Pin("AREF", "AREF", PinHeader.DigitalTop, "External reference voltage for analogRead().", new Vector2(x, topY), PinCapability.AnalogReference)); x -= k_Pitch;
            pins.Add(Pin("SDA", "SDA", PinHeader.DigitalTop, "I2C data line (same signal as A4).", new Vector2(x, topY), PinCapability.I2cSda)); x -= k_Pitch;
            pins.Add(Pin("SCL", "SCL", PinHeader.DigitalTop, "I2C clock line (same signal as A5).", new Vector2(x, topY), PinCapability.I2cScl));

            // Bottom power header, left to right.
            const float bottomY = 2.54f;
            x = 27.9f;
            pins.Add(Pin("IOREF", "IOREF", PinHeader.PowerBottom, "Reports the board I/O voltage (5 V).", new Vector2(x, bottomY), PinCapability.Power)); x += k_Pitch;
            pins.Add(Pin("RESET", "RST", PinHeader.PowerBottom, "Pull LOW to reset the microcontroller.", new Vector2(x, bottomY), PinCapability.Reset)); x += k_Pitch;
            pins.Add(Pin("3V3", "3.3V", PinHeader.PowerBottom, "3.3 V regulated output (max ~50 mA).", new Vector2(x, bottomY), PinCapability.Power)); x += k_Pitch;
            pins.Add(Pin("5V", "5V", PinHeader.PowerBottom, "5 V regulated output for sensors and modules.", new Vector2(x, bottomY), PinCapability.Power)); x += k_Pitch;
            pins.Add(Pin("GND1", "GND", PinHeader.PowerBottom, "Ground (0 V) reference.", new Vector2(x, bottomY), PinCapability.Ground)); x += k_Pitch;
            pins.Add(Pin("GND2", "GND", PinHeader.PowerBottom, "Ground (0 V) reference.", new Vector2(x, bottomY), PinCapability.Ground)); x += k_Pitch;
            pins.Add(Pin("VIN", "VIN", PinHeader.PowerBottom, "Raw input voltage (7-12 V) from the barrel jack.", new Vector2(x, bottomY), PinCapability.Power));

            // Bottom analog header: A0..A5.
            x = 50.8f;
            for (int a = 0; a <= 5; a++, x += k_Pitch)
            {
                var caps = new List<string> { PinCapability.AnalogInput, PinCapability.DigitalAlt };
                string desc = $"Analog input (10-bit ADC, 0-1023); also usable as digital pin D{14 + a}.";
                if (a == 4) { caps.Add(PinCapability.I2cSda); desc += " I2C SDA."; }
                if (a == 5) { caps.Add(PinCapability.I2cScl); desc += " I2C SCL."; }
                pins.Add(Pin($"A{a}", $"A{a}", PinHeader.AnalogBottom, desc, new Vector2(x, bottomY), caps.ToArray()));
            }
            return pins;
        }

        static PinRecord DigitalPin(int d, Vector2 pos)
        {
            var caps = new List<string> { PinCapability.Digital };
            string desc = "Digital I/O (digitalRead/digitalWrite).";
            switch (d)
            {
                case 0: caps.Add(PinCapability.SerialRx); desc += " Serial RX."; break;
                case 1: caps.Add(PinCapability.SerialTx); desc += " Serial TX."; break;
                case 2: caps.Add(PinCapability.Interrupt); desc += " Interrupt INT0."; break;
                case 3: caps.Add(PinCapability.Pwm); caps.Add(PinCapability.Interrupt); desc += " PWM (~), interrupt INT1."; break;
                case 5: case 6: case 9: caps.Add(PinCapability.Pwm); desc += " PWM (~)."; break;
                case 10: caps.Add(PinCapability.Pwm); caps.Add(PinCapability.SpiSS); desc += " PWM (~), SPI SS."; break;
                case 11: caps.Add(PinCapability.Pwm); caps.Add(PinCapability.SpiMosi); desc += " PWM (~), SPI MOSI."; break;
                case 12: caps.Add(PinCapability.SpiMiso); desc += " SPI MISO."; break;
                case 13: caps.Add(PinCapability.SpiSck); caps.Add(PinCapability.BuiltinLed); desc += " SPI SCK, on-board LED."; break;
            }
            string label = caps.Contains(PinCapability.Pwm) ? $"~{d}" : d.ToString();
            return Pin($"D{d}", label, PinHeader.DigitalTop, desc, pos, caps.ToArray());
        }
    }
}
