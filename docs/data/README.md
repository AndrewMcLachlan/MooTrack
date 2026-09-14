# Recorded working hours

**This directory is deliberately not in the repository.** Its contents describe a
real person's movements — when they started, stopped, locked the machine and
walked away — at minute resolution, over months. That is not something to publish.

Everything here is ignored by git except this file.

## What belongs here

    daily-hours.csv                 19 weekdays at session precision — the acceptance set
    weekly-hours.csv                two-year weekly series with quality flags
    weekly-hours-sleepstudy.csv     four-week rollup from the sleep study
    raw/sleepstudy28.html           28-day sleep study, XML embedded in HTML
    raw/sleepstudy-report.json      14-day sleep study, JSON variant
    raw/battery-report.xml          two years of weekly history
    raw/battery.html                human-readable form of the above

## Why it matters

`daily-hours.csv` is the acceptance test. The derivation engine, run in
display-only mode over `raw/sleepstudy28.html`, must reproduce all 20 of its rows
exactly. If the model disagrees with the CSV, the model is wrong — do not edit the
CSV to match.

The two acceptance tests are **skipped, not failed**, when this data is absent, so
a clean checkout and CI both build green. Anywhere the data is present they run
and hold the model to it.

## Regenerating

The sleep study window has already rolled past 2026-08-17, so that file cannot be
recreated. Weekly battery history survives for years; sleep study data does not.

    powercfg /sleepstudy    /output "%TEMP%\sleepstudy.html" /duration 28
    powercfg /batteryreport /output "%TEMP%\battery.html"
    powercfg /batteryreport /output "%TEMP%\battery-report.xml" /xml

Archive fresh copies periodically.

## Pointing the tests elsewhere

Set `MOOTRACK_VALIDATION_DATA` to a directory holding these files and the
acceptance tests will use it instead of this one.
