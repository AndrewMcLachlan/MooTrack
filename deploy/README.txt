MooTrack agent
==============

Records screen-on, user presence and session events so working hours can be
derived later. Nothing is computed here: this writes an append-only journal and
the hours are worked out elsewhere, from that journal.

Everything needed is in this folder. There is no installer and nothing is
downloaded.


INSTALL
-------

1. Copy this whole folder to the machine.

2. Open PowerShell AS ADMINISTRATOR and change to this folder.

3. Run:

       powershell -ExecutionPolicy Bypass -File .\Install-MooTrackAgent.ps1

   Or, to point it at the collector at the same time:

       powershell -ExecutionPolicy Bypass -File .\Install-MooTrackAgent.ps1 -CollectorUrl https://collector:8088 -ApiKey "your-key"

The script waits up to 90 seconds after starting the service and reports how
many observations actually reached the journal, so you know it is capturing
rather than merely running.


WHAT IT PUTS WHERE
------------------

  C:\Program Files\MooTrack\MooTrack.Agent.exe     the service
  C:\ProgramData\MooTrack\journal\                 the record, one file per day
  C:\ProgramData\MooTrack\appsettings.json         configuration
  Service "MooTrack", LocalSystem, automatic start

Configuration lives under ProgramData rather than beside the executable so that
reinstalling or upgrading cannot discard the collector URL and API key.


SEEING YOUR HOURS
-----------------

       powershell -ExecutionPolicy Bypass -File .\Show-MooTrackHours.ps1

Derives hours from the local journal and prints a daily table, and writes
daily-hours.csv, weekly-hours.csv and mootrack-hours.xlsx to
%USERPROFILE%\MooTrack-hours.

This is the same engine the collector runs, so the figures match what the NAS
will produce. Nothing needs to be deployed for this to work.

To see the effect of a different break policy, recompute without re-recording:

       powershell -ExecutionPolicy Bypass -File .\Show-MooTrackHours.ps1 -BridgeMinutes 5

Gaps shorter than the bridge count as working time. The default is 10 minutes.


CHECKING IT
-----------

       powershell -ExecutionPolicy Bypass -File .\Check-MooTrackAgent.ps1

Read-only. Shows service state, how recently the journal was written, a
breakdown of what was captured today, and recent log entries.


UPGRADING
---------

Re-run Install-MooTrackAgent.ps1 with a newer folder. The service is stopped,
the executable replaced and the service restarted. Configuration and journal are
left alone.


REMOVING IT
-----------

       powershell -ExecutionPolicy Bypass -File .\Uninstall-MooTrackAgent.ps1

Removes the service, the executable and the event log source.

THE JOURNAL IS DELIBERATELY NOT REMOVED. It is the substantiation record and is
not the software's to delete. The script prints where it is.


IF IT WILL NOT START
--------------------

Check the log first:

       Get-EventLog -LogName Application -Source MooTrack -Newest 20

"cannot follow Security" is expected and harmless if it appears on a machine
where the Security event log is restricted. That source is corroboration only;
capture continues from the power and session notifications, which is where the
hours actually come from.
