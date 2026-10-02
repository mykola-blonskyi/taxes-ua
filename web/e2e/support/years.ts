// The first quarter has no annex 1, so a spec that downloads the declaration file uses it. The file is
// built only once the quarter has ended (Rule 15), so from 1 January to 31 March it is last year's.
export function endedFirstQuarterYear(today: string): number {
  const year = Number(today.slice(0, 4));
  return Number(today.slice(5, 7)) <= 3 ? year - 1 : year;
}
