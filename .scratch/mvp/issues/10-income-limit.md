# 10: Income limit

GitHub: #11
Status: ready-for-agent
Blocked by: #10

## Parent

#1

## What to build

On the home screen, a bar shows the year's income against the annual limit with a percentage. At 85% a warning appears; at 100% a message about exceeding the limit, with the excess amount and the tax at the excess rate. The limit is not prorated for a partial year.

## Acceptance criteria

- [ ] Engine tests at 84.99%, 85%, 100%, and an excess of one kopeck.
- [ ] The bar changes color at the thresholds; the text names the amount left before the threshold.
- [ ] The dashboard API response carries a limit status matching the engine.

## Blocked by

- #10 (Home screen: the next step)
