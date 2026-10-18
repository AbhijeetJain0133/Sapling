namespace Sapling.Shared.Components;

/// <summary>
/// Inline SVG illustrations, all built from the logo's three primitives so the whole app reads as Sapling:
///   1. rounded stem bars (rect with rx = half the width),
///   2. the leaf sweep (the logo's exact path, placed with a translate/rotate/scale transform),
///   3. seed circles (var(--brand-seed), one per scene as the single warm accent).
/// Depth comes from two or three translucent shapes stacked at .12 / .22 / .35 plus one blurred blob; texture is
/// a dotted grid or three diagonal lines, never both. Colours are only var(--g-a), var(--g-b), var(--brand-*),
/// and white with opacity, so a scene recolours with the .hue-* class of its container and follows dark mode.
/// Every id carries {id}, replaced per instance, because SVG ids are document-wide.
/// </summary>
public static class ArtLibrary
{
    /// <summary>The logo's leaf. In logo space its root (where it meets the stem) is at about (512, 680).</summary>
    public const string LeafPath = "M512.49,680.17s122.65-392.5,376.72-331.97c0,0-37.11,330.13-376.72,331.97Z";

    /// <summary>Places the leaf with its root at (x, y), rotated by deg, at scale s (0.1 ≈ 75px long).</summary>
    private static string Leaf(double x, double y, double deg, double s, string fill = "url(#{id}-leaf)", string extra = "") =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"<path d=\"{{leaf}}\" fill=\"{fill}\" transform=\"translate({x} {y}) rotate({deg}) scale({s}) translate(-512 -680)\" {extra}/>");

    private static string Defs(string more = "") =>
        """
        <defs>
          <linearGradient id="{id}-g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="var(--g-a)"/><stop offset="1" stop-color="var(--g-b)"/></linearGradient>
          <linearGradient id="{id}-leaf" x1="0" y1="1" x2="1" y2="0"><stop offset="0" stop-color="var(--brand-stem)"/><stop offset="1" stop-color="var(--brand-leaf)"/></linearGradient>
          <filter id="{id}-blur" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="18"/></filter>
          <pattern id="{id}-dots" width="10" height="10" patternUnits="userSpaceOnUse"><circle cx="1.5" cy="1.5" r="1.5" fill="#fff" fill-opacity=".35"/></pattern>
          MORE
        </defs>
        """.Replace("MORE", more);

    private const string Open = """<svg viewBox="0 0 240 200" xmlns="http://www.w3.org/2000/svg" role="presentation" focusable="false">""";
    private const string OpenWide = """<svg viewBox="0 0 400 260" xmlns="http://www.w3.org/2000/svg" role="presentation" focusable="false">""";

    private static readonly Dictionary<string, string> Scenes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Compass: dial of stacked rings, four stem-bar needles, the leaf pointing north, seed at the pivot.
        ["career"] = Open + Defs() + """
            <ellipse cx="128" cy="112" rx="78" ry="60" fill="var(--g-b)" opacity=".55" filter="url(#{id}-blur)"/>
            <rect x="12" y="140" width="80" height="48" fill="url(#{id}-dots)"/>
            <path d="M20 170 C 70 90, 150 60, 228 40" fill="none" stroke="#fff" stroke-opacity=".18" stroke-width="3" stroke-linecap="round"/>
            <path d="M40 186 C 90 120, 170 96, 232 84" fill="none" stroke="#fff" stroke-opacity=".10" stroke-width="3" stroke-linecap="round" stroke-dasharray="8 10"/>
            <circle cx="120" cy="104" r="64" fill="#fff" fill-opacity=".12"/>
            <circle cx="120" cy="104" r="50" fill="url(#{id}-g)" opacity=".9"/>
            <circle cx="120" cy="104" r="50" fill="none" stroke="#fff" stroke-opacity=".5" stroke-width="3"/>
            <g fill="#fff" fill-opacity=".85">
              <rect x="115" y="60" width="10" height="34" rx="5"/>
              <rect x="115" y="114" width="10" height="34" rx="5" fill-opacity=".45"/>
              <rect x="76" y="99" width="34" height="10" rx="5" fill-opacity=".45"/>
              <rect x="130" y="99" width="34" height="10" rx="5" fill-opacity=".45"/>
            </g>
            """ + Leaf(120, 104, -8, 0.095) + """
            <circle cx="120" cy="104" r="9" fill="var(--brand-seed)"/>
            <circle cx="120" cy="104" r="9" fill="none" stroke="#fff" stroke-opacity=".9" stroke-width="2.5"/>
            </svg>
            """,

        // Winding path with milestones; the last one sprouts a leaf.
        ["roadmap"] = Open + Defs() + """
            <ellipse cx="120" cy="120" rx="90" ry="56" fill="var(--g-b)" opacity=".5" filter="url(#{id}-blur)"/>
            <rect x="150" y="14" width="76" height="52" fill="url(#{id}-dots)"/>
            <path d="M24 168 C 60 168, 60 108, 100 108 S 140 160, 180 150 S 210 70, 222 44" fill="none" stroke="#fff" stroke-opacity=".22" stroke-width="14" stroke-linecap="round"/>
            <path d="M24 168 C 60 168, 60 108, 100 108 S 140 160, 180 150 S 210 70, 222 44" fill="none" stroke="#fff" stroke-opacity=".9" stroke-width="4" stroke-linecap="round" stroke-dasharray="10 10"/>
            <circle cx="24" cy="168" r="13" fill="#fff" fill-opacity=".9"/><circle cx="24" cy="168" r="6" fill="url(#{id}-g)"/>
            <circle cx="100" cy="108" r="13" fill="#fff" fill-opacity=".9"/><circle cx="100" cy="108" r="6" fill="url(#{id}-g)"/>
            <circle cx="180" cy="150" r="13" fill="#fff" fill-opacity=".9"/><circle cx="180" cy="150" r="6" fill="url(#{id}-g)"/>
            <rect x="216" y="44" width="12" height="58" rx="6" fill="var(--brand-stem)"/>
            """ + Leaf(222, 62, 0, 0.09) + """
            <circle cx="222" cy="40" r="11" fill="var(--brand-seed)"/>
            </svg>
            """,

        // Briefcase on rising stem bars.
        ["opportunities"] = Open + Defs() + """
            <ellipse cx="120" cy="120" rx="86" ry="58" fill="var(--g-b)" opacity=".5" filter="url(#{id}-blur)"/>
            <rect x="14" y="20" width="70" height="46" fill="url(#{id}-dots)"/>
            <g fill="#fff">
              <rect x="150" y="120" width="18" height="60" rx="9" fill-opacity=".35"/>
              <rect x="178" y="92" width="18" height="88" rx="9" fill-opacity=".55"/>
              <rect x="206" y="60" width="18" height="120" rx="9" fill-opacity=".8"/>
            </g>
            <circle cx="215" cy="46" r="11" fill="var(--brand-seed)"/>
            <rect x="34" y="86" width="118" height="84" rx="16" fill="#fff" fill-opacity=".18"/>
            <rect x="42" y="94" width="102" height="70" rx="12" fill="url(#{id}-g)"/>
            <rect x="42" y="94" width="102" height="70" rx="12" fill="none" stroke="#fff" stroke-opacity=".6" stroke-width="3"/>
            <rect x="72" y="76" width="42" height="24" rx="8" fill="none" stroke="#fff" stroke-opacity=".85" stroke-width="6"/>
            <rect x="42" y="124" width="102" height="4" fill="#fff" fill-opacity=".5"/>
            <rect x="84" y="118" width="18" height="16" rx="4" fill="#fff" fill-opacity=".95"/>
            """ + Leaf(38, 92, 12, 0.06) + """
            </svg>
            """,

        // Document with a tick badge; the leaf peeks from behind.
        ["resume"] = Open + Defs() + """
            <ellipse cx="116" cy="112" rx="80" ry="62" fill="var(--g-b)" opacity=".5" filter="url(#{id}-blur)"/>
            <g stroke="#fff" stroke-opacity=".18" stroke-width="3" stroke-linecap="round"><path d="M18 60 L58 20"/><path d="M18 84 L82 20"/><path d="M30 96 L106 20"/></g>
            """ + Leaf(70, 150, 20, 0.11) + """
            <rect x="70" y="34" width="104" height="134" rx="14" fill="#fff" fill-opacity=".22" transform="rotate(-6 122 101)"/>
            <rect x="78" y="30" width="104" height="134" rx="14" fill="#fff" fill-opacity=".95"/>
            <rect x="94" y="52" width="46" height="9" rx="4.5" fill="url(#{id}-g)"/>
            <g fill="var(--g-a)" fill-opacity=".28">
              <rect x="94" y="74" width="72" height="7" rx="3.5"/><rect x="94" y="90" width="64" height="7" rx="3.5"/>
              <rect x="94" y="106" width="70" height="7" rx="3.5"/><rect x="94" y="122" width="48" height="7" rx="3.5"/>
            </g>
            <circle cx="176" cy="42" r="19" fill="var(--brand-seed)"/>
            <circle cx="176" cy="42" r="19" fill="none" stroke="#fff" stroke-width="3"/>
            <path d="M167 42 l6 6 l12 -12" fill="none" stroke="#fff" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
            """,

        // Microphone with sound arcs; the leaf is the stand's foot.
        ["interview"] = Open + Defs() + """
            <ellipse cx="120" cy="104" rx="80" ry="64" fill="var(--g-b)" opacity=".5" filter="url(#{id}-blur)"/>
            <rect x="160" y="140" width="70" height="46" fill="url(#{id}-dots)"/>
            <g fill="none" stroke="#fff" stroke-linecap="round" stroke-width="4">
              <path d="M62 78 a 40 40 0 0 0 0 52" stroke-opacity=".6"/><path d="M44 66 a 62 62 0 0 0 0 76" stroke-opacity=".32"/><path d="M26 54 a 84 84 0 0 0 0 100" stroke-opacity=".14"/>
              <path d="M178 78 a 40 40 0 0 1 0 52" stroke-opacity=".6"/><path d="M196 66 a 62 62 0 0 1 0 76" stroke-opacity=".32"/><path d="M214 54 a 84 84 0 0 1 0 100" stroke-opacity=".14"/>
            </g>
            <rect x="98" y="34" width="44" height="86" rx="22" fill="#fff" fill-opacity=".22"/>
            <rect x="104" y="40" width="32" height="74" rx="16" fill="url(#{id}-g)"/>
            <rect x="104" y="40" width="32" height="74" rx="16" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="3"/>
            <path d="M84 96 a 36 36 0 0 0 72 0" fill="none" stroke="#fff" stroke-opacity=".9" stroke-width="5" stroke-linecap="round"/>
            <rect x="115" y="132" width="10" height="34" rx="5" fill="var(--brand-stem)"/>
            """ + Leaf(120, 168, 0, 0.075) + """
            <circle cx="120" cy="30" r="8" fill="var(--brand-seed)"/>
            </svg>
            """,

        // Pillared building with a leaf growing from the apex.
        ["govt"] = Open + Defs() + """
            <ellipse cx="120" cy="120" rx="90" ry="54" fill="var(--g-b)" opacity=".5" filter="url(#{id}-blur)"/>
            <rect x="26" y="150" width="188" height="38" fill="url(#{id}-dots)"/>
            <rect x="40" y="150" width="160" height="14" rx="7" fill="#fff" fill-opacity=".9"/>
            <g fill="#fff" fill-opacity=".85"><rect x="60" y="88" width="20" height="60" rx="10"/><rect x="110" y="88" width="20" height="60" rx="10"/><rect x="160" y="88" width="20" height="60" rx="10"/></g>
            <rect x="46" y="72" width="148" height="12" rx="6" fill="#fff" fill-opacity=".95"/>
            <path d="M120 24 L192 70 L48 70 Z" fill="url(#{id}-g)"/>
            <path d="M120 24 L192 70 L48 70 Z" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="3" stroke-linejoin="round"/>
            """ + Leaf(120, 30, -10, 0.07) + """
            <circle cx="120" cy="24" r="8" fill="var(--brand-seed)"/>
            </svg>
            """,

        // Two speech bubbles; seed dots as people.
        ["community"] = Open + Defs() + """
            <ellipse cx="120" cy="108" rx="88" ry="60" fill="var(--g-b)" opacity=".5" filter="url(#{id}-blur)"/>
            <g stroke="#fff" stroke-opacity=".16" stroke-width="3" stroke-linecap="round"><path d="M180 180 L226 134"/><path d="M196 186 L232 150"/><path d="M212 190 L234 168"/></g>
            <path d="M40 56 h104 a16 16 0 0 1 16 16 v50 a16 16 0 0 1 -16 16 h-58 l-26 22 v-22 h-20 a16 16 0 0 1 -16 -16 v-50 a16 16 0 0 1 16 -16z" fill="#fff" fill-opacity=".95"/>
            <g fill="url(#{id}-g)"><circle cx="66" cy="96" r="9"/><circle cx="92" cy="96" r="9"/><circle cx="118" cy="96" r="9"/></g>
            <path d="M120 96 h80 a14 14 0 0 1 14 14 v42 a14 14 0 0 1 -14 14 h-14 v20 l-24 -20 h-42 a14 14 0 0 1 -14 -14 v-42 a14 14 0 0 1 14 -14z" fill="url(#{id}-g)"/>
            <path d="M120 96 h80 a14 14 0 0 1 14 14 v42 a14 14 0 0 1 -14 14 h-14 v20 l-24 -20 h-42 a14 14 0 0 1 -14 -14 v-42 a14 14 0 0 1 14 -14z" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="3"/>
            <g fill="#fff" fill-opacity=".9"><rect x="134" y="118" width="60" height="7" rx="3.5"/><rect x="134" y="134" width="40" height="7" rx="3.5"/></g>
            """ + Leaf(36, 146, 24, 0.07) + """
            <circle cx="200" cy="92" r="9" fill="var(--brand-seed)"/>
            </svg>
            """,

        // Avatar silhouette inside a ring, leaf behind the shoulder.
        ["profile"] = Open + Defs() + """
            <ellipse cx="120" cy="110" rx="80" ry="64" fill="var(--g-b)" opacity=".5" filter="url(#{id}-blur)"/>
            <rect x="160" y="20" width="66" height="44" fill="url(#{id}-dots)"/>
            <circle cx="120" cy="104" r="70" fill="#fff" fill-opacity=".12"/>
            <circle cx="120" cy="104" r="58" fill="none" stroke="#fff" stroke-opacity=".55" stroke-width="4"/>
            """ + Leaf(158, 150, 30, 0.09) + """
            <circle cx="120" cy="86" r="24" fill="#fff" fill-opacity=".95"/>
            <path d="M74 158 a46 46 0 0 1 92 0z" fill="#fff" fill-opacity=".95"/>
            <circle cx="120" cy="86" r="24" fill="url(#{id}-g)" opacity=".9"/>
            <path d="M80 158 a40 40 0 0 1 80 0z" fill="url(#{id}-g)" opacity=".9"/>
            <circle cx="164" cy="64" r="10" fill="var(--brand-seed)"/>
            <circle cx="164" cy="64" r="10" fill="none" stroke="#fff" stroke-opacity=".9" stroke-width="2.5"/>
            </svg>
            """,

        // Onboarding 1: a pencil writing a line.
        ["onboarding-1"] = Open + Defs() + """
            <ellipse cx="120" cy="110" rx="84" ry="60" fill="var(--g-b)" opacity=".5" filter="url(#{id}-blur)"/>
            <rect x="20" y="20" width="70" height="46" fill="url(#{id}-dots)"/>
            <rect x="44" y="60" width="152" height="110" rx="16" fill="#fff" fill-opacity=".95"/>
            <g fill="var(--g-a)" fill-opacity=".25"><rect x="62" y="84" width="80" height="8" rx="4"/><rect x="62" y="104" width="116" height="8" rx="4"/><rect x="62" y="124" width="96" height="8" rx="4"/></g>
            <rect x="62" y="144" width="52" height="8" rx="4" fill="url(#{id}-g)"/>
            <g transform="rotate(-40 172 118)"><rect x="160" y="70" width="24" height="86" rx="6" fill="url(#{id}-g)"/><path d="M160 156 l12 22 l12 -22z" fill="#fff" fill-opacity=".95"/><rect x="160" y="70" width="24" height="14" rx="6" fill="var(--brand-seed)"/></g>
            """ + Leaf(50, 66, 14, 0.06) + """
            </svg>
            """,

        // Onboarding 2: stacked skill chips.
        ["onboarding-2"] = Open + Defs() + """
            <ellipse cx="120" cy="110" rx="84" ry="60" fill="var(--g-b)" opacity=".5" filter="url(#{id}-blur)"/>
            <rect x="160" y="140" width="66" height="44" fill="url(#{id}-dots)"/>
            <g>
              <rect x="40" y="52" width="92" height="30" rx="15" fill="#fff" fill-opacity=".95"/><rect x="56" y="63" width="60" height="8" rx="4" fill="url(#{id}-g)"/>
              <rect x="142" y="52" width="60" height="30" rx="15" fill="#fff" fill-opacity=".5"/>
              <rect x="40" y="94" width="66" height="30" rx="15" fill="#fff" fill-opacity=".5"/>
              <rect x="116" y="94" width="86" height="30" rx="15" fill="url(#{id}-g)"/><rect x="116" y="94" width="86" height="30" rx="15" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="3"/><rect x="132" y="105" width="52" height="8" rx="4" fill="#fff" fill-opacity=".9"/>
              <rect x="40" y="136" width="98" height="30" rx="15" fill="#fff" fill-opacity=".95"/><rect x="56" y="147" width="64" height="8" rx="4" fill="var(--g-a)" fill-opacity=".35"/>
            </g>
            <circle cx="150" cy="150" r="13" fill="var(--brand-seed)"/>
            <path d="M144 150 l4 4 l8 -8" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
            """ + Leaf(196, 44, 20, 0.06) + """
            </svg>
            """,

        // Onboarding 3: a dial of interests.
        ["onboarding-3"] = Open + Defs() + """
            <ellipse cx="120" cy="110" rx="84" ry="60" fill="var(--g-b)" opacity=".5" filter="url(#{id}-blur)"/>
            <g stroke="#fff" stroke-opacity=".16" stroke-width="3" stroke-linecap="round"><path d="M20 60 L60 20"/><path d="M20 84 L84 20"/></g>
            <path d="M48 140 a72 72 0 0 1 144 0" fill="none" stroke="#fff" stroke-opacity=".22" stroke-width="22" stroke-linecap="round"/>
            <path d="M48 140 a72 72 0 0 1 144 0" fill="none" stroke="url(#{id}-g)" stroke-width="12" stroke-linecap="round" stroke-dasharray="150 400"/>
            <g fill="#fff" fill-opacity=".95"><circle cx="48" cy="140" r="7"/><circle cx="69" cy="90" r="7"/><circle cx="120" cy="68" r="7"/><circle cx="171" cy="90" r="7"/><circle cx="192" cy="140" r="7"/></g>
            <rect x="115" y="90" width="10" height="52" rx="5" fill="var(--brand-stem)" transform="rotate(-28 120 140)"/>
            <circle cx="120" cy="140" r="13" fill="#fff" fill-opacity=".95"/>
            <circle cx="120" cy="140" r="7" fill="var(--brand-seed)"/>
            """ + Leaf(176, 176, 26, 0.06) + """
            </svg>
            """,

        // An empty pot with the first sprout.
        ["nothing-here"] = Open + Defs() + """
            <ellipse cx="120" cy="124" rx="80" ry="50" fill="var(--g-b)" opacity=".45" filter="url(#{id}-blur)"/>
            <rect x="44" y="150" width="152" height="36" fill="url(#{id}-dots)"/>
            <path d="M72 108 h96 l-10 68 a10 10 0 0 1 -10 9 h-56 a10 10 0 0 1 -10 -9z" fill="url(#{id}-g)"/>
            <rect x="64" y="98" width="112" height="18" rx="9" fill="#fff" fill-opacity=".95"/>
            <rect x="115" y="56" width="10" height="46" rx="5" fill="var(--brand-stem)"/>
            """ + Leaf(120, 74, -6, 0.06) + """
            <circle cx="120" cy="54" r="8" fill="var(--brand-seed)"/>
            </svg>
            """,

        // Magnifier with the leaf as its handle.
        ["search"] = Open + Defs() + """
            <ellipse cx="120" cy="104" rx="80" ry="60" fill="var(--g-b)" opacity=".45" filter="url(#{id}-blur)"/>
            <rect x="20" y="140" width="70" height="46" fill="url(#{id}-dots)"/>
            <circle cx="108" cy="88" r="52" fill="#fff" fill-opacity=".14"/>
            <circle cx="108" cy="88" r="40" fill="#fff" fill-opacity=".9"/>
            <circle cx="108" cy="88" r="40" fill="none" stroke="url(#{id}-g)" stroke-width="10"/>
            <path d="M80 70 a28 28 0 0 1 22 -12" fill="none" stroke="var(--g-a)" stroke-opacity=".3" stroke-width="5" stroke-linecap="round"/>
            <rect x="138" y="118" width="14" height="60" rx="7" fill="var(--brand-stem)" transform="rotate(-45 145 148)"/>
            """ + Leaf(170, 172, 40, 0.08) + """
            <circle cx="108" cy="88" r="7" fill="var(--brand-seed)"/>
            </svg>
            """,

        // A grown sapling in concentric rings.
        ["success"] = Open + Defs() + """
            <ellipse cx="120" cy="110" rx="84" ry="64" fill="var(--g-b)" opacity=".5" filter="url(#{id}-blur)"/>
            <circle cx="120" cy="110" r="84" fill="#fff" fill-opacity=".08"/><circle cx="120" cy="110" r="62" fill="#fff" fill-opacity=".12"/><circle cx="120" cy="110" r="40" fill="#fff" fill-opacity=".16"/>
            <rect x="112" y="80" width="16" height="96" rx="8" fill="var(--brand-stem)"/>
            """ + Leaf(120, 118, -4, 0.11) + Leaf(120, 140, 4, 0.085, "url(#{id}-leaf)", "style=\"transform-box:fill-box\"").Replace("scale(0.085)", "scale(-0.085 0.085)") + """
            <circle cx="120" cy="72" r="14" fill="var(--brand-seed)"/>
            <circle cx="120" cy="72" r="14" fill="none" stroke="#fff" stroke-opacity=".9" stroke-width="3"/>
            </svg>
            """,

        // A signpost with drooping leaf.
        ["not-found"] = Open + Defs() + """
            <ellipse cx="120" cy="120" rx="84" ry="54" fill="var(--g-b)" opacity=".45" filter="url(#{id}-blur)"/>
            <rect x="140" y="150" width="76" height="40" fill="url(#{id}-dots)"/>
            <rect x="112" y="60" width="16" height="122" rx="8" fill="var(--brand-stem)"/>
            <path d="M126 70 h70 l14 14 l-14 14 h-70z" fill="#fff" fill-opacity=".95"/>
            <path d="M114 108 h-70 l-14 14 l14 14 h70z" fill="url(#{id}-g)"/>
            <rect x="140" y="80" width="40" height="8" rx="4" fill="var(--g-a)" fill-opacity=".35"/>
            <rect x="60" y="118" width="40" height="8" rx="4" fill="#fff" fill-opacity=".8"/>
            """ + Leaf(120, 62, 150, 0.07) + """
            <circle cx="120" cy="54" r="10" fill="var(--brand-seed)"/>
            </svg>
            """,

        // Landing hero: the logo geometry oversized, two ghost leaves, hue blobs.
        ["auth-hero"] = OpenWide + Defs("""
            <linearGradient id="{id}-c" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="var(--hue-career-a)"/><stop offset="1" stop-color="var(--hue-career-b)"/></linearGradient>
            <linearGradient id="{id}-r" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="var(--hue-roadmap-a)"/><stop offset="1" stop-color="var(--hue-roadmap-b)"/></linearGradient>
            """) + """
            <ellipse cx="150" cy="150" rx="120" ry="90" fill="url(#{id}-r)" opacity=".28" filter="url(#{id}-blur)"/>
            <ellipse cx="290" cy="90" rx="110" ry="80" fill="url(#{id}-c)" opacity=".28" filter="url(#{id}-blur)"/>
            <rect x="20" y="190" width="120" height="60" fill="url(#{id}-dots)" opacity=".8"/>
            <g stroke="var(--hue-career-a)" stroke-opacity=".14" stroke-width="3" stroke-linecap="round"><path d="M300 250 L380 170"/><path d="M322 254 L388 188"/><path d="M344 256 L392 208"/></g>
            """ + Leaf(160, 236, -6, 0.26, "url(#{id}-c)", "opacity=\".14\"") + Leaf(120, 246, 8, 0.2, "url(#{id}-r)", "opacity=\".18\"") + """
            <rect x="182" y="106" width="42" height="140" rx="21" fill="var(--brand-stem)"/>
            """ + Leaf(203, 140, 0, 0.3) + """
            <circle cx="203" cy="98" r="26" fill="var(--brand-seed)"/>
            <circle cx="203" cy="98" r="26" fill="none" stroke="#fff" stroke-opacity=".6" stroke-width="4"/>
            <g fill="#fff" fill-opacity=".9"><circle cx="330" cy="200" r="5"/><circle cx="80" cy="120" r="4"/><circle cx="350" cy="60" r="3"/></g>
            </svg>
            """,

        // Sapling AI: the sapling growing inside a speech bubble, the seed as a spark, a reply bubble beside it.
        ["assistant"] = Open + Defs() + """
            <ellipse cx="118" cy="104" rx="90" ry="62" fill="var(--g-b)" opacity=".5" filter="url(#{id}-blur)"/>
            <rect x="14" y="134" width="70" height="50" fill="url(#{id}-dots)"/>
            <path d="M48 34 h108 a20 20 0 0 1 20 20 v56 a20 20 0 0 1 -20 20 h-58 l-30 26 v-26 h-20 a20 20 0 0 1 -20 -20 v-56 a20 20 0 0 1 20 -20z" fill="#fff" fill-opacity=".96"/>
            <rect x="84" y="68" width="14" height="46" rx="7" fill="var(--brand-stem)"/>
            """ + Leaf(92, 86, 0, 0.1) + """
            <path d="M176 22 l6 15 15 6 -15 6 -6 15 -6 -15 -15 -6 15 -6z" fill="var(--brand-seed)" stroke="#fff" stroke-opacity=".9" stroke-width="2.5" stroke-linejoin="round"/>
            <path d="M206 64 l3 7 7 3 -7 3 -3 7 -3 -7 -7 -3 7 -3z" fill="#fff" fill-opacity=".9"/>
            <path d="M132 118 h70 a14 14 0 0 1 14 14 v28 a14 14 0 0 1 -14 14 h-10 v18 l-22 -18 h-38 a14 14 0 0 1 -14 -14 v-28 a14 14 0 0 1 14 -14z" fill="url(#{id}-g)"/>
            <path d="M132 118 h70 a14 14 0 0 1 14 14 v28 a14 14 0 0 1 -14 14 h-10 v18 l-22 -18 h-38 a14 14 0 0 1 -14 -14 v-28 a14 14 0 0 1 14 -14z" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="3"/>
            <g fill="#fff" fill-opacity=".92"><circle cx="148" cy="146" r="5"/><circle cx="167" cy="146" r="5"/><circle cx="186" cy="146" r="5"/></g>
            </svg>
            """,
    };

    public static IEnumerable<string> Names => Scenes.Keys;

    public static string Render(string? name, string uid)
    {
        var svg = name is not null && Scenes.TryGetValue(name, out var s) ? s : Scenes["nothing-here"];
        return svg.Replace("{leaf}", LeafPath).Replace("{id}", uid);
    }
}
