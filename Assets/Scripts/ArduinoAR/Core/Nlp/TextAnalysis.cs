using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ArduinoAR
{
    /// <summary>Topic catalogue: capabilities, keyword weights and display names.</summary>
    public static class Topics
    {
        public const string Pwm = "pwm", Digital = "digital", Analog = "analog", Power = "power", Ground = "ground",
            Reset = "reset", Serial = "serial", I2c = "i2c", Spi = "spi", Interrupt = "interrupt", Led = "led",
            Aref = "aref", Board = "board";

        public sealed class Topic
        {
            public string Id;
            public string Title;
            public string[] Capabilities;
            public (string keyword, int weight)[] Keywords;
        }

        public static readonly Topic[] All =
        {
            new() { Id = Pwm, Title = "PWM pins", Capabilities = new[] { PinCapability.Pwm },
                Keywords = new[] { ("pwm", 5), ("analogwrite", 5), ("pulse width", 5), ("duty cycle", 4), ("analog output", 4), ("~", 3), ("fade", 3), ("dim", 3), ("dimming", 3), ("brightness", 3), ("motor speed", 3), ("servo", 3) } },
            new() { Id = Digital, Title = "Digital I/O pins", Capabilities = new[] { PinCapability.Digital },
                Keywords = new[] { ("digital", 4), ("digitalwrite", 5), ("digitalread", 5), ("gpio", 4), ("pinmode", 4), ("button", 2), ("relay", 2), ("buzzer", 2), ("high or low", 3) } },
            new() { Id = Analog, Title = "Analog input pins", Capabilities = new[] { PinCapability.AnalogInput },
                Keywords = new[] { ("analog input", 5), ("analog in", 5), ("analogread", 5), ("adc", 5), ("analog", 3), ("potentiometer", 4), ("sensor", 2), ("ldr", 4), ("light sensor", 4), ("temperature", 3) } },
            new() { Id = Power, Title = "Power pins", Capabilities = new[] { PinCapability.Power },
                Keywords = new[] { ("power", 5), ("5v", 4), ("3.3v", 4), ("3v3", 4), ("vin", 4), ("vcc", 4), ("supply", 3), ("voltage", 2) } },
            new() { Id = Ground, Title = "Ground pins", Capabilities = new[] { PinCapability.Ground },
                Keywords = new[] { ("ground", 5), ("gnd", 5), ("0v", 3) } },
            new() { Id = Reset, Title = "Reset pin", Capabilities = new[] { PinCapability.Reset },
                Keywords = new[] { ("reset", 5), ("rst", 4), ("restart", 3) } },
            new() { Id = Serial, Title = "Serial (UART) pins", Capabilities = new[] { PinCapability.SerialRx, PinCapability.SerialTx },
                Keywords = new[] { ("serial", 5), ("uart", 5), ("usart", 5), ("tx", 4), ("rx", 4), ("baud", 4), ("bluetooth", 3), ("gps", 3) } },
            new() { Id = I2c, Title = "I2C pins", Capabilities = new[] { PinCapability.I2cSda, PinCapability.I2cScl },
                Keywords = new[] { ("i2c", 5), ("iic", 5), ("twi", 5), ("sda", 5), ("scl", 5), ("wire library", 4), ("oled", 3), ("lcd", 3), ("rtc", 3), ("mpu6050", 3),
                                   // Common breakout modules that are wired over I2C.
                                   ("bme280", 4), ("bmp280", 4), ("bmp180", 4), ("ds3231", 4), ("ssd1306", 4), ("pcf8574", 4), ("ads1115", 4), ("bh1750", 4), ("vl53l0x", 4) } },
            new() { Id = Spi, Title = "SPI pins", Capabilities = new[] { PinCapability.SpiSS, PinCapability.SpiMosi, PinCapability.SpiMiso, PinCapability.SpiSck },
                Keywords = new[] { ("spi", 5), ("mosi", 5), ("miso", 5), ("sck", 5), ("chip select", 4), ("sd card", 3), ("rfid", 3) } },
            new() { Id = Interrupt, Title = "Interrupt pins", Capabilities = new[] { PinCapability.Interrupt },
                Keywords = new[] { ("interrupt", 5), ("interrupts", 5), ("attachinterrupt", 5), ("isr", 4), ("encoder", 3) } },
            new() { Id = Led, Title = "Built-in LED pin", Capabilities = new[] { PinCapability.BuiltinLed },
                Keywords = new[] { ("built in led", 5), ("builtin led", 5), ("onboard led", 5), ("on board led", 5), ("led_builtin", 5), ("blink", 3), ("led", 2) } },
            new() { Id = Aref, Title = "AREF pin", Capabilities = new[] { PinCapability.AnalogReference },
                Keywords = new[] { ("aref", 5), ("analog reference", 5), ("reference voltage", 4) } },
            new() { Id = Board, Title = "Board information", Capabilities = new string[0],
                Keywords = new[] { ("which board", 5), ("what board", 5), ("microcontroller", 4), ("mcu", 4), ("processor", 4), ("atmega", 4), ("usb chip", 4), ("ftdi", 4),
                                   ("usb to serial", 6), ("16u2", 5), ("ft232rl", 5), ("crystal", 4), ("clock speed", 4), ("clock", 3), ("mhz", 4),
                                   ("flash", 5), ("sram", 5), ("eeprom", 5), ("memory", 4), ("operating voltage", 6), ("logic voltage", 5),
                                   ("dimensions", 5), ("how big", 5), ("how large", 5), ("how small", 5), ("board size", 5), ("connectors", 5), ("connector", 4), ("icsp", 4), ("power jack", 4),
                                   ("usb port", 5), ("usb connector", 5), ("usb socket", 5), ("ioref", 4),
                                   ("about this board", 4), ("about the board", 4), ("spduino", 3) } },
        };

        public static Topic Get(string id) => All.FirstOrDefault(t => t.Id == id);

        /// <summary>Natural reading order for answers: D0..D13, then A0..A5, then named pins.</summary>
        public static int PinOrder(string pinId)
        {
            if (pinId.Length > 1 && int.TryParse(pinId.Substring(1), out int n))
            {
                if (pinId[0] == 'D') return n;
                if (pinId[0] == 'A') return 100 + n;
            }
            return 1000;
        }

        public static string ForCapability(string cap) => cap switch
        {
            PinCapability.Pwm => Pwm,
            PinCapability.Digital or PinCapability.DigitalAlt => Digital,
            PinCapability.AnalogInput => Analog,
            PinCapability.SerialRx or PinCapability.SerialTx => Serial,
            PinCapability.I2cSda or PinCapability.I2cScl => I2c,
            PinCapability.SpiSS or PinCapability.SpiMosi or PinCapability.SpiMiso or PinCapability.SpiSck => Spi,
            PinCapability.Interrupt => Interrupt,
            PinCapability.BuiltinLed => Led,
            PinCapability.Power => Power,
            PinCapability.Ground => Ground,
            PinCapability.Reset => Reset,
            PinCapability.AnalogReference => Aref,
            _ => null,
        };

        public static string CapabilityName(string cap) => cap switch
        {
            PinCapability.Pwm => "PWM output (analogWrite)",
            PinCapability.Digital => "digital I/O",
            PinCapability.DigitalAlt => "digital I/O (as an extra digital pin)",
            PinCapability.AnalogInput => "analog input (ADC)",
            PinCapability.SerialRx => "UART serial RX",
            PinCapability.SerialTx => "UART serial TX",
            PinCapability.I2cSda => "I2C SDA (data)",
            PinCapability.I2cScl => "I2C SCL (clock)",
            PinCapability.SpiSS => "SPI SS (chip select)",
            PinCapability.SpiMosi => "SPI MOSI",
            PinCapability.SpiMiso => "SPI MISO",
            PinCapability.SpiSck => "SPI SCK (clock)",
            PinCapability.Interrupt => "an external interrupt",
            PinCapability.BuiltinLed => "the on-board LED (LED_BUILTIN)",
            PinCapability.Power => "power supply",
            PinCapability.Ground => "ground (0 V)",
            PinCapability.Reset => "reset",
            PinCapability.AnalogReference => "the analog reference input",
            _ => cap,
        };
    }

    /// <summary>Entities extracted from a question.</summary>
    public sealed class ExtractedEntities
    {
        /// <summary>Pin ids referenced explicitly ("pin 9", "D13", "A0", "the 5V pin").</summary>
        public readonly List<string> PinIds = new();
        /// <summary>Technical terms found (topic keywords and concept words).</summary>
        public readonly List<string> Terms = new();
        /// <summary>Normalised text with pin references removed, so "digital pin 3" or "the VIN pin" don't count as topics.</summary>
        public string TopicText = "";
        public bool HasPronounReference;
        public bool HasFollowUpMarker;
        /// <summary>Board profile ids named in the question ("uno", "spduino"), in order of appearance.</summary>
        public readonly List<string> BoardMentions = new();
        /// <summary>"Arduino" without "Uno": only meaningful as the second board of a comparison.</summary>
        public bool MentionsGenericArduino;
        /// <summary>compare / difference / vs / like the ...</summary>
        public bool HasCompareCue;
        /// <summary>connect / wire / hook up / attach / plug in ...</summary>
        public bool HasConnectCue;
        /// <summary>A built-in feature asked about that no board here documents ("wifi", "bluetooth", ...), or null.</summary>
        public string AskedFeature;
        /// <summary>Single I2C role asked for ("which one is SDA?"): a <see cref="PinCapability"/> or null.</summary>
        public string RoleCapability;
        /// <summary>Board-fact attributes the question asks about (see <see cref="FactTags"/>).</summary>
        public readonly List<string> FactTags = new();
        /// <summary>Circuit component named in the question (see <see cref="CircuitGuides"/>), or null.</summary>
        public string Component;
    }

    /// <summary>Normalisation and entity extraction. Deterministic and offline.</summary>
    public static class TextAnalysis
    {
        // Separators speech-to-text or typing put between spelled-out letters: "i-2-c", "p.w.m", "s d a".
        const string k_Sep = @"[\s.\-_]+";
        // Word start that is not inside a contraction ("what's d a" is not "s d a").
        const string k_Start = @"(?<![\w'’])";

        /// <summary>
        /// Speech-to-text forms of technical terms, applied before punctuation is stripped. Each pattern needs
        /// the whole spoken form ("i squared c", "eye two see", "p w m"), so ordinary words are left alone.
        /// </summary>
        static readonly (Regex pattern, string replacement)[] s_SpokenForms =
        {
            (new Regex(k_Start + @"i\s*²\s*c\b|" + k_Start + @"i\s*\^\s*2\s*c\b"), "i2c"),
            (new Regex(k_Start + @"(?:i|eye)[\s.\-_]*2[\s.\-_]*(?:c|see|sea)\b"), "i2c"),                     // i 2 c, i-2-c, i2 c, eye 2 see
            (new Regex(k_Start + @"(?:i|eye)" + k_Sep + @"(?:squared?|two|to|too)" + k_Sep + @"(?:c|see|sea)\b"), "i2c"), // i squared c, i two c, eye to see
            (new Regex(k_Start + @"i" + k_Sep + @"i" + k_Sep + @"c\b"), "iic"),
            (new Regex(k_Start + @"p" + k_Sep + @"w" + k_Sep + @"m\b"), "pwm"),
            (new Regex(k_Start + @"s" + k_Sep + @"p" + k_Sep + @"i\b"), "spi"),
            (new Regex(k_Start + @"s" + k_Sep + @"d" + k_Sep + @"a\b"), "sda"),
            (new Regex(k_Start + @"s" + k_Sep + @"c" + k_Sep + @"l\b"), "scl"),
            (new Regex(k_Start + @"s" + k_Sep + @"c" + k_Sep + @"k\b"), "sck"),
            (new Regex(k_Start + @"g" + k_Sep + @"n" + k_Sep + @"d\b"), "gnd"),
            (new Regex(k_Start + @"a" + k_Sep + @"d" + k_Sep + @"c\b"), "adc"),
            (new Regex(k_Start + @"u" + k_Sep + @"a" + k_Sep + @"r" + k_Sep + @"t\b"), "uart"),
            (new Regex(k_Start + @"l" + k_Sep + @"e" + k_Sep + @"d\b"), "led"),
            (new Regex(k_Start + @"v" + k_Sep + @"i" + k_Sep + @"n\b"), "vin"),
            (new Regex(k_Start + @"m" + k_Sep + @"o" + k_Sep + @"s" + k_Sep + @"i\b"), "mosi"),
            (new Regex(k_Start + @"m" + k_Sep + @"i" + k_Sep + @"s" + k_Sep + @"o\b"), "miso"),
            (new Regex(k_Start + @"s" + k_Sep + @"d" + k_Sep + @"card\b"), "sd card"),
        };

        static readonly string[] s_NumberWords =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
        };
        const string k_PinNumber = @"\b(?:[ad]\s*)?(?:zero|one(?!\s+of\b)|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|\d{1,2})\b";
        // Pin numbers only directly after "pin"/"pins" (optionally "number"), e.g. "pin five", "analog pin a zero",
        // "pins three and five". "Two pins", "pin to ground" or "one of the pins" are not pin references.
        static readonly Regex s_PinNumberContext = new(
            @"\b(pins?)(?:\s+numbers?)?\s+(" + k_PinNumber + @"(?:\s*(?:and|or)?\s*" + k_PinNumber + @")*)");
        // "\b" so the "d" at the end of "and" is never read as a D-pin prefix; spaces are only consumed after
        // an "a"/"d" prefix ("a zero" -> "a0"), so "3 5 and 6" keeps its separators.
        static readonly Regex s_PinNumberToken = new(@"\b(?:([ad])\s*)?(zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|\d{1,2})\b");

        static string NormalizePinNumbers(string s) =>
            s_PinNumberContext.Replace(s, m =>
                m.Groups[1].Value + " " + s_PinNumberToken.Replace(m.Groups[2].Value, n =>
                {
                    string word = n.Groups[2].Value;
                    int index = System.Array.IndexOf(s_NumberWords, word);
                    return n.Groups[1].Value + (index >= 0 ? index.ToString() : word);
                }));

        static readonly (Regex pattern, string replacement)[] s_Normalizations =
        {
            (new Regex(@"\banalogue\b"), "analog"),
            (new Regex(@"pulse[\s-]*width[\s-]*modulation"), "pwm pulse width"),
            (new Regex(@"\bbuilt-in\b"), "built in"),
            (new Regex(@"\bon-board\b"), "on board"),
            (new Regex(@"\bi/o\b"), "io"),
            (new Regex(@"\b3\.3\s*(?:v|volts?)\b"), "3.3v"),
            (new Regex(@"\b5\s*(?:v|volts?)\b|\bfive volts?\b"), "5v"),
            // Any other spoken/written voltage ("15 volts", "9 volt") in the same compact form, so rating
            // questions are recognised whatever the number.
            (new Regex(@"\b(\d{1,2}(?:\.\d)?)\s*volts?\b"), "$1v"),
            (new Regex(@"\bwhat's\b|\bwhats\b"), "what is"),
            (new Regex(@"\bhow's\b"), "how is"),
            (new Regex(@"[^a-z0-9~_.\s]"), " "),
            (new Regex(@"(?<![0-9])\.|\.(?![0-9])"), " "),
            (new Regex(@"\s+"), " "),
        };

        public static string Normalize(string text)
        {
            string s = (text ?? "").ToLowerInvariant();
            foreach (var (pattern, replacement) in s_SpokenForms) s = pattern.Replace(s, replacement);
            foreach (var (pattern, replacement) in s_Normalizations) s = pattern.Replace(s, replacement);
            s = NormalizePinNumbers(s);
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        public static bool ContainsPhrase(string normalized, string phrase)
        {
            if (phrase == "~") return normalized.Contains("~");
            return Regex.IsMatch(" " + normalized + " ", $@"(?<=\s){Regex.Escape(phrase)}(?=\s)");
        }

        static readonly Regex s_PinList = new(@"\bpins?\s+((?:[ad]?\d{1,2}\s*(?:(?:,|and|or|&)\s*)?)+)");
        static readonly Regex s_SinglePin = new(@"\b(?:(digital\s+pin|analog\s+pin|pin)\s*([ad]?)(\d{1,2})|([da])(\d{1,2}))\b");
        static readonly string[] s_Pronouns = { "it", "its", "that", "those", "them", "this", "these", "they", "same" };
        static readonly string[] s_FollowUpStarts = { "what about", "how about", "and ", "what of", "same for", "also ", "then ",
                                                      "tell me more", "more about", "explain that", "explain it", "explain more" };
        static readonly string[] s_QuestionWords = { "what", "which", "where", "how", "why", "explain", "does", "do", "is", "are", "can", "list", "show", "tell", "define" };

        // Named pins become pin entities only when the question talks about "the X pin".
        static readonly (string phrase, string pinId)[] s_NamedPins =
        {
            ("5v", "5V"), ("3.3v", "3V3"), ("3v3", "3V3"), ("vin", "VIN"), ("aref", "AREF"), ("ioref", "IOREF"), ("rst", "RESET"), ("reset", "RESET"),
        };

        public static ExtractedEntities Extract(string normalized, BoardProfile board)
        {
            var e = new ExtractedEntities();
            void AddPin(string id)
            {
                if (board != null && board.Pins.All(p => p.Id != id)) return;
                if (!e.PinIds.Contains(id)) e.PinIds.Add(id);
            }

            foreach (Match m in s_PinList.Matches(normalized))
            {
                // "analog pins 2 and 3" -> A2, A3; plain "pins 2 and 3" -> D2, D3.
                bool analogList = normalized.Substring(0, m.Index).TrimEnd().EndsWith("analog");
                foreach (Match n in Regex.Matches(m.Groups[1].Value, @"([ad]?)(\d{1,2})"))
                    AddPin((n.Groups[1].Value == "a" || (analogList && n.Groups[1].Value == "") ? "A" : "D") + int.Parse(n.Groups[2].Value));
            }

            foreach (Match m in s_SinglePin.Matches(normalized))
            {
                if (m.Groups[3].Success)
                {
                    bool analog = m.Groups[1].Value.StartsWith("analog") || m.Groups[2].Value == "a";
                    AddPin((analog ? "A" : "D") + int.Parse(m.Groups[3].Value));
                }
                else AddPin((m.Groups[4].Value == "a" ? "A" : "D") + int.Parse(m.Groups[5].Value));
            }

            // Text used for topic scoring: drop the pin references themselves.
            // Single references first, so "digital pin 3" is removed as a whole.
            string topicText = s_SinglePin.Replace(normalized, " ");
            topicText = s_PinList.Replace(topicText, " ");

            bool talksAboutPin = Regex.IsMatch(normalized, @"\bpin\b");
            if (talksAboutPin)
                foreach (var (phrase, pinId) in s_NamedPins)
                    // A named pin the board doesn't have (IOREF on the SPDUINO) stays a topic word, so the
                    // question can still be answered from the board facts.
                    if (ContainsPhrase(normalized, phrase) && (board == null || board.Pins.Any(p => p.Id == pinId)))
                    {
                        AddPin(pinId);
                        topicText = Regex.Replace(" " + topicText + " ", $@"(?<=\s){Regex.Escape(phrase)}(?=\s)", " ");
                    }
            e.TopicText = Regex.Replace(topicText, @"\s+", " ").Trim();

            foreach (var topic in Topics.All)
                foreach (var (keyword, _) in topic.Keywords)
                    if (ContainsPhrase(normalized, keyword) && !e.Terms.Contains(keyword)) e.Terms.Add(keyword);

            // "this board" / "that Arduino" name the board in view; they are not references to the previous answer.
            var tokens = s_BoardPhrase.Replace(normalized, " ").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            e.HasPronounReference = tokens.Any(t => s_Pronouns.Contains(t));
            // Fragments like "pin 5?" or "A0" with no question word, or bare "which pins?", only make sense
            // against the previous turn.
            var allTokens = normalized.Split(' ');
            bool fragment = allTokens.Length <= 3 && (!allTokens.Any(t => s_QuestionWords.Contains(t)) || allTokens.All(t => s_BareQuestionWords.Contains(t)));
            e.HasFollowUpMarker = s_FollowUpStarts.Any(normalized.StartsWith) || fragment;

            ExtractBoardLevel(normalized, e);
            return e;
        }

        static readonly Regex s_BoardPhrase = new(@"\b(?:this|that|the)\s+(?:board|arduino|uno|spduino|microcontroller)\b");
        static readonly string[] s_BareQuestionWords = { "which", "what", "where", "pins", "pin", "ones", "are", "they", "list", "show", "them" };

        // Board names as typed or as speech-to-text heard them ("SPDUINO" -> "speed no", "Speedo").
        static readonly (Regex pattern, string boardId)[] s_BoardNames =
        {
            (new Regex(@"\b(?:arduino\s+)?uno\b"), "arduino_uno_r3"),
            (new Regex(@"\b(?:spduino|sp\s*duino|s\s*p\s*duino|spa\s*duino|speedo|speed\s+no|speed\s+we\s+know)\b"), SpduinoProfile.Id),
        };
        static readonly Regex s_GenericArduino = new(@"\barduino\b(?!\s+uno\b)");
        static readonly Regex s_CompareCue = new(@"\b(?:compare|compared|comparison|difference|differences|different|differ|differs|vs|versus|like the|same as)\b");
        static readonly Regex s_ConnectCue = new(@"\b(?:connect|connecting|connected|connection|connections|wire|wires|wiring|wired|hook|hooks|hooked|hooking|hookup|attach|attaching|plug in|plug)\b");
        static readonly Regex s_WireLibrary = new(@"\bwire\s+(?:library|lib|h)\b");
        static readonly Regex s_Possession =new(@"\b(?:have|has|having|got|built in|builtin|on board|onboard|include|includes|come with|comes with|support|supports)\b");
        static readonly (Regex pattern, string name)[] s_Features =
        {
            (new Regex(@"\bwi\s*fi\b|\bwifi\b"), "WiFi"),
            (new Regex(@"\bbluetooth\b|\bble\b"), "Bluetooth"),
            (new Regex(@"\bethernet\b"), "Ethernet"),
            (new Regex(@"\bcamera\b"), "a camera"),
            (new Regex(@"\bgps\b"), "GPS"),
            (new Regex(@"\bnfc\b"), "NFC"),
            (new Regex(@"\b(?:lte|4g|5g|gsm|cellular)\b"), "cellular"),
            (new Regex(@"\blora\b|\bzigbee\b"), "LoRa/Zigbee"),
        };
        static readonly (Regex pattern, string tag)[] s_FactTagWords =
        {
            (new Regex(@"\b(?:usb|ftdi|ft232rl|16u2)\b"), FactTags.Usb),
            (new Regex(@"\b(?:microcontroller|mcu|processor|atmega\w*)\b"), FactTags.Mcu),
            (new Regex(@"\b(?:clock|crystal|mhz)\b"), FactTags.Clock),
            (new Regex(@"\b(?:flash|sram|eeprom|memory|ram|storage)\b"), FactTags.Memory),
            (new Regex(@"\b(?:operating|logic)\s+voltage\b|\blogic level\b"), FactTags.Voltage),
            (new Regex(@"\b(?:connectors?|jack|icsp|ports?|socket|reset button)\b"), FactTags.Connectors),
            (new Regex(@"\b(?:dimensions|how big|how large|how small|large|measure|measures|length|width)\b|\bsize\b"), FactTags.Size),
            (new Regex(@"\bioref\b"), FactTags.Headers),
        };

        /// <summary>Board ids named in a normalised question, in order of appearance.</summary>
        public static List<string> BoardMentions(string normalized)
        {
            var found = new List<(int index, string id)>();
            foreach (var (pattern, id) in s_BoardNames)
            {
                var m = pattern.Match(normalized);
                if (m.Success && found.All(f => f.id != id)) found.Add((m.Index, id));
            }
            return found.OrderBy(f => f.index).Select(f => f.id).ToList();
        }

        static void ExtractBoardLevel(string normalized, ExtractedEntities e)
        {
            e.BoardMentions.AddRange(BoardMentions(normalized));
            e.MentionsGenericArduino = s_GenericArduino.IsMatch(normalized);
            e.HasCompareCue = s_CompareCue.IsMatch(normalized);
            // "the Wire library" / "Wire.h" is Arduino's I2C library, not the verb "to wire".
            e.HasConnectCue = s_ConnectCue.IsMatch(s_WireLibrary.Replace(normalized, " "));
            e.Component = CircuitGuides.DetectComponent(normalized);

            // "Does this board have WiFi?" - a built-in feature, not "how do I connect a GPS module".
            if (!e.HasConnectCue && s_Possession.IsMatch(normalized))
                foreach (var (pattern, name) in s_Features)
                    if (pattern.IsMatch(normalized)) { e.AskedFeature = name; break; }

            // "Which one is SDA?" / "where is the SCL pin" - exactly one I2C role.
            bool sda = ContainsPhrase(normalized, "sda"), scl = ContainsPhrase(normalized, "scl");
            if (sda != scl) e.RoleCapability = sda ? PinCapability.I2cSda : PinCapability.I2cScl;

            foreach (var (pattern, tag) in s_FactTagWords)
                if (pattern.IsMatch(normalized) && !e.FactTags.Contains(tag)) e.FactTags.Add(tag);
            // "Which chip is on it?" is the microcontroller, unless it is the USB chip.
            if (ContainsPhrase(normalized, "chip") && !e.FactTags.Contains(FactTags.Usb) && !e.FactTags.Contains(FactTags.Mcu)) e.FactTags.Add(FactTags.Mcu);
            // "flash memory size" asks about memory, not the board's size.
            if (e.FactTags.Contains(FactTags.Memory)) e.FactTags.Remove(FactTags.Size);
        }

        public static IEnumerable<string> Tokens(string normalized) => normalized.Split(' ').Where(t => t.Length > 1);
    }
}
