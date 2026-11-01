using System.Collections.Generic;
using System.Linq;

namespace ArduinoAR
{
    public enum FactKind { Concept, PinCapability, PinSummary, Board }

    /// <summary>One retrievable piece of knowledge, always tied to a board (or general) plus its evidence.</summary>
    public sealed class KnowledgeFact
    {
        public string Id;
        /// <summary>Board profile id, or null for general concept knowledge.</summary>
        public string BoardId;
        public FactKind Kind;
        /// <summary>Topic id (see <see cref="Topics"/>), e.g. "pwm".</summary>
        public string Topic;
        /// <summary>Pin id for pin facts, e.g. "D3".</summary>
        public string PinId;
        /// <summary>Capability tag for <see cref="FactKind.PinCapability"/> facts.</summary>
        public string Capability;
        public string Text;
        public string Source;
        public VerificationStatus Status;
        /// <summary>Extra words used by the lexical retriever.</summary>
        public string[] Keywords = new string[0];
        /// <summary>Board-fact attribute tags (see <see cref="FactTags"/>).</summary>
        public string[] Tags = new string[0];

        public override string ToString() => $"{Id} [{Status}] {Text}";
    }

    /// <summary>
    /// Structured, board-aware knowledge base. Pin facts are generated from the board profiles (one fact
    /// per pin capability, with that capability's own verification status); concept facts are general
    /// explanations that deliberately avoid board-specific electrical ratings.
    /// </summary>
    public static class KnowledgeBase
    {
        const string k_GeneralSource = "General electronics / Arduino language reference";
        /// <summary>Concept topic of the general I2C wiring guidance (not a question topic).</summary>
        public const string I2cWiringTopic = "i2c_wiring";

        static readonly List<KnowledgeFact> s_Concepts = new()
        {
            Concept("pwm", "Pulse-width modulation (PWM) switches a digital pin fully on and off very quickly. The fraction of each cycle that the pin is HIGH (the duty cycle) sets the average power delivered. On Arduino, analogWrite(pin, value) takes 0-255, from 0% to 100% duty cycle. It is used for LED dimming, motor speed control and similar tasks. The output is still a digital square wave, not a true analog voltage; add a filter if a steady voltage is needed.",
                "duty", "cycle", "analogwrite", "dimming", "fade", "brightness", "motor", "speed", "square", "wave"),
            Concept("digital", "A digital pin is either HIGH or LOW. Configure it with pinMode(pin, OUTPUT or INPUT), then use digitalWrite(pin, HIGH/LOW) to drive it or digitalRead(pin) to read it. INPUT_PULLUP enables the internal pull-up resistor, which is useful for buttons wired to ground.",
                "pinmode", "digitalwrite", "digitalread", "high", "low", "input", "output", "pullup", "button"),
            Concept("analog", "An analog input pin measures a voltage with the analog-to-digital converter (ADC). analogRead(pin) returns 0-1023 on ATmega328P-based boards (10-bit ADC), scaled against the analog reference voltage. Typical uses are potentiometers, light sensors (LDRs) and other analog sensors.",
                "adc", "analogread", "converter", "voltage", "potentiometer", "sensor", "ldr", "measure"),
            Concept("serial", "UART (hardware serial) sends data one bit at a time over TX (transmit) and RX (receive). Connect TX of one device to RX of the other, share ground, and use the same baud rate on both sides, e.g. Serial.begin(9600). On many Arduino-compatible boards the hardware serial pins are shared with the USB connection, so disconnect external devices from them while uploading.",
                "uart", "tx", "rx", "baud", "transmit", "receive", "usb", "bluetooth", "gps", "monitor"),
            Concept("i2c", "I2C is a two-wire bus: SDA carries data and SCL carries the clock. Several devices can share the bus because each has its own address. The bus needs pull-up resistors (many modules include them). On Arduino use the Wire library. OLED displays, RTC modules and IMU sensors commonly use I2C.",
                "sda", "scl", "wire", "address", "bus", "pullup", "oled", "display", "rtc", "sensor"),
            // General wiring guidance; board-specific pins come from the board profile. No voltage or current ratings.
            Concept(I2cWiringTopic, "To wire an I2C module, connect its VCC to the supply voltage its datasheet specifies (5V or 3.3V), GND to GND, SDA to the board's SDA line and SCL to the SCL line. SDA and SCL need pull-up resistors; many modules include them. A module whose pins only tolerate 3.3 V may need a logic-level shifter on a 5 V board.",
                "wiring", "connect", "module", "vcc", "pullup", "level", "shifter"),
            Concept("spi", "SPI is a fast, full-duplex bus with MOSI (controller out), MISO (controller in) and SCK (clock), plus one chip-select (CS/SS) line per device. On Arduino use the SPI library. SD-card modules, RFID readers and some displays use SPI.",
                "mosi", "miso", "sck", "clock", "chip", "select", "cs", "ss", "sd", "card", "rfid"),
            Concept("interrupt", "An external interrupt runs a function immediately when a pin changes, instead of polling it in loop(). Use attachInterrupt(digitalPinToInterrupt(pin), handler, mode) where mode is RISING, FALLING, CHANGE or LOW. Keep interrupt handlers short.",
                "attachinterrupt", "isr", "handler", "rising", "falling", "change", "encoder", "event"),
            Concept("led", "LED_BUILTIN is the Arduino constant for the board's on-board LED pin, used by the Blink example: pinMode(LED_BUILTIN, OUTPUT) then digitalWrite(LED_BUILTIN, HIGH).",
                "led_builtin", "blink", "onboard", "builtin", "light"),
            Concept("power", "Power pins supply circuits: 5V and 3.3V provide regulated voltages, VIN is the board's raw input voltage, and GND is the 0 V return. Do not connect an external supply to the 5V or 3.3V pins unless the board documentation allows it, and never short a supply pin to GND.",
                "supply", "voltage", "vin", "5v", "3.3v", "regulator", "battery"),
            Concept("ground", "GND is the 0 V reference. Every circuit connected to the board must share its ground, otherwise signals have no common reference.",
                "gnd", "reference", "common", "negative", "0v"),
            Concept("reset", "Pulling the RESET pin LOW restarts the microcontroller and the sketch, exactly like pressing the reset button.",
                "rst", "restart", "reboot", "button"),
            Concept("aref", "AREF sets an external reference voltage for analogRead(); select it in code with analogReference(EXTERNAL). Never apply a voltage above the board's operating voltage to AREF.",
                "reference", "analogreference", "external", "adc"),
        };

        static KnowledgeFact Concept(string topic, string text, params string[] keywords) =>
            new() { Id = "concept." + topic, BoardId = null, Kind = FactKind.Concept, Topic = topic, Text = text, Source = k_GeneralSource, Status = VerificationStatus.General, Keywords = keywords };

        static readonly Dictionary<string, List<KnowledgeFact>> s_ByBoard = new();

        public static IReadOnlyList<KnowledgeFact> Concepts => s_Concepts;

        /// <summary>All facts usable for a board: its own pin/board facts plus general concepts.</summary>
        public static IReadOnlyList<KnowledgeFact> For(BoardProfile board)
        {
            if (board == null) return s_Concepts;
            if (!s_ByBoard.TryGetValue(board.Id, out var facts))
            {
                facts = new List<KnowledgeFact>(BuildBoardFacts(board));
                facts.AddRange(s_Concepts);
                s_ByBoard[board.Id] = facts;
            }
            return facts;
        }

        public static KnowledgeFact ConceptFor(string topic) => s_Concepts.FirstOrDefault(c => c.Topic == topic);

        static IEnumerable<KnowledgeFact> BuildBoardFacts(BoardProfile board)
        {
            int i = 0;
            foreach (var fact in board.BoardFacts)
                yield return new KnowledgeFact
                {
                    Id = $"{board.Id}.board.{i++}", BoardId = board.Id, Kind = FactKind.Board, Topic = Topics.Board,
                    Text = fact.Text, Source = fact.Source, Status = fact.Status, Tags = fact.Tags ?? new string[0],
                };

            foreach (var pin in board.Pins)
            {
                yield return new KnowledgeFact
                {
                    Id = $"{board.Id}.{pin.Id}", BoardId = board.Id, Kind = FactKind.PinSummary, PinId = pin.Id,
                    Text = $"{pin.Id} (label \"{pin.Label}\"): {pin.Description}",
                    Source = pin.InferredCapabilities.Count == 0 ? board.ConfirmedSource : $"{board.ConfirmedSource}; partly {board.InferredSource}",
                    Status = pin.InferredCapabilities.Count == 0 ? board.ConfirmedStatus : VerificationStatus.Inferred,
                    Keywords = new[] { pin.Id.ToLowerInvariant(), pin.Label.ToLowerInvariant() },
                };
                foreach (var cap in pin.Capabilities)
                {
                    bool inferred = pin.InferredCapabilities.Contains(cap);
                    yield return new KnowledgeFact
                    {
                        Id = $"{board.Id}.{pin.Id}.{cap}", BoardId = board.Id, Kind = FactKind.PinCapability,
                        PinId = pin.Id, Capability = cap, Topic = Topics.ForCapability(cap),
                        Text = $"{pin.Id} provides {Topics.CapabilityName(cap)}.",
                        Source = inferred ? board.InferredSource : board.ConfirmedSource,
                        Status = inferred ? VerificationStatus.Inferred : board.ConfirmedStatus,
                        Keywords = new[] { pin.Id.ToLowerInvariant(), cap },
                    };
                }
            }
        }

        public static string StatusLabel(VerificationStatus s) => s switch
        {
            VerificationStatus.VerifiedOnBoard => "verified on this board",
            VerificationStatus.Documented => "documented",
            VerificationStatus.Inferred => "UNVERIFIED (inferred)",
            _ => "general knowledge",
        };
    }
}
