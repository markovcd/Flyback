/**
 * Who a request counts as, for the rate limits and for one rating each: the address
 * Cloudflare saw, with an IPv6 address counted as its /64, since one household is
 * handed a whole /64 and could use a fresh address per request.
 */
export function visitorOf(request: Request): string {
  const address = request.headers.get("CF-Connecting-IP")?.trim();

  if (!address) return "unknown";
  if (!address.includes(":")) return address;

  const groups = parse(address);
  if (groups === null) return address;

  // An IPv4 address carried in IPv6 counts as itself.
  if (groups.slice(0, 5).every((g) => g === 0) && groups[5] === 0xffff)
    return `${groups[6]! >> 8}.${groups[6]! & 255}.${groups[7]! >> 8}.${groups[7]! & 255}`;

  return format([...groups.slice(0, 4), 0, 0, 0, 0]) + "/64";
}

function parse(address: string): number[] | null {
  let text = address.split("%")[0]!;
  const tail: number[] = [];

  // A dotted IPv4 ending, as in ::ffff:192.0.2.1.
  const dotted = /^(.*:)(\d+)\.(\d+)\.(\d+)\.(\d+)$/.exec(text);
  if (dotted) {
    const parts = dotted.slice(2).map(Number);
    if (parts.some((p) => p > 255)) return null;
    tail.push((parts[0]! << 8) | parts[1]!, (parts[2]! << 8) | parts[3]!);
    const prefix = dotted[1]!;
    text = prefix.endsWith("::") ? prefix : prefix.slice(0, -1);
  }

  const halves = text.split("::");
  if (halves.length > 2) return null;

  const read = (half: string): number[] | null => {
    if (half === "") return [];
    const out: number[] = [];
    for (const group of half.split(":")) {
      if (!/^[0-9a-fA-F]{1,4}$/.test(group)) return null;
      out.push(parseInt(group, 16));
    }
    return out;
  };

  const head = read(halves[0]!);
  const rest = halves.length === 2 ? read(halves[1]!) : [];
  if (head === null || rest === null) return null;

  const known = head.length + rest.length + tail.length;
  if (halves.length === 1 && known !== 8) return null;
  if (halves.length === 2 && known > 7) return null;

  return [...head, ...new Array<number>(8 - known).fill(0), ...rest, ...tail];
}

/** RFC 5952, as .NET writes an IPv6 address: lower case, the longest run of zero groups as ::. */
function format(groups: number[]): string {
  let best = -1;
  let length = 0;

  for (let i = 0; i < 8; ) {
    if (groups[i] !== 0) {
      i++;
      continue;
    }
    let j = i;
    while (j < 8 && groups[j] === 0) j++;
    if (j - i > length && j - i >= 2) {
      best = i;
      length = j - i;
    }
    i = j;
  }

  const hexed = groups.map((g) => g.toString(16));
  if (best < 0) return hexed.join(":");

  return hexed.slice(0, best).join(":") + "::" + hexed.slice(best + length).join(":");
}
