import qrcode from "qrcode-generator";

// The library writes only the low byte of each character unless told otherwise, which turns Cyrillic into
// the wrong bytes and miscounts it against the capacity. The bank reads the content as UTF-8.
const utf8 = new TextEncoder();
qrcode.stringToBytes = (text) => Array.from(utf8.encode(text));

const QUIET_ZONE = 4;
const MIN_VERSION = 10;
const MAX_VERSION = 17;
const LEVELS = ["Q", "M"] as const;

type Level = (typeof LEVELS)[number];

// NBU Resolution 97, Appendix 1, II.11: diameter of the white circle under the hryvnia sign, by QR version.
const signCircleModules: Record<number, number> = {
  10: 17,
  11: 19,
  12: 19,
  13: 21,
  14: 23,
  15: 23,
  16: 25,
  17: 25,
};

export type NbuQr = {
  size: number;
  version: number;
  level: Level;
  isDark: (row: number, col: number) => boolean;
};

function tryEncode(content: string, level: Level): NbuQr | null {
  let qr = qrcode(0, level);
  qr.addData(content, "Byte");
  try {
    qr.make();
  } catch {
    // The library throws, instead of choosing a version, when the data is longer than the largest code holds.
    return null;
  }
  let version = (qr.getModuleCount() - 17) / 4;
  if (version < MIN_VERSION) {
    qr = qrcode(MIN_VERSION, level);
    qr.addData(content, "Byte");
    qr.make();
    version = MIN_VERSION;
  }
  if (version > MAX_VERSION) return null;
  return { size: qr.getModuleCount(), version, level, isDark: (row, col) => qr.isDark(row, col) };
}

export function encodeNbuQr(content: string): NbuQr | null {
  for (const level of LEVELS) {
    const encoded = tryEncode(content, level);
    if (encoded) return encoded;
  }
  return null;
}

export function NbuQrCode({ content, label }: { content: string; label: string }) {
  const qr = encodeNbuQr(content);
  if (!qr) return null;

  const total = qr.size + QUIET_ZONE * 2;
  const centre = total / 2;
  const circle = signCircleModules[qr.version]!;
  const inner = circle - 4;

  let path = "";
  for (let row = 0; row < qr.size; row++) {
    for (let col = 0; col < qr.size; col++) {
      if (qr.isDark(row, col)) path += `M${col + QUIET_ZONE} ${row + QUIET_ZONE}h1v1h-1z`;
    }
  }

  return (
    // A scanner needs dark-on-light, so the code stays black on white in the dark theme too.
    <div className="w-full rounded-lg bg-white p-2">
      <svg
        role="img"
        aria-label={label}
        data-qr-version={qr.version}
        data-qr-level={qr.level}
        viewBox={`0 0 ${total} ${total}`}
        shapeRendering="crispEdges"
        className="block h-auto w-full"
      >
        <rect width={total} height={total} fill="white" />
        <path d={path} fill="black" />
        <circle cx={centre} cy={centre} r={circle / 2} fill="white" />
        <text
          x={centre}
          y={centre}
          fill="black"
          fontFamily="sans-serif"
          fontSize={inner * 0.8}
          textAnchor="middle"
          dominantBaseline="central"
        >
          ₴
        </text>
      </svg>
    </div>
  );
}
