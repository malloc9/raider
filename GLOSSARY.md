# Raider

A Windows desktop tool that drives Raid: Shadow Legends accounts on the Plarium Play client, performing routine in-game work without the player.

## Language

**Account**:
One Raid: Shadow Legends player profile, reached by logging into Plarium Play with one Plarium login.
_Avoid_: Profile, user, player

**Chore**:
One self-contained piece of routine in-game work, such as claiming playtime rewards or collecting the Gem Mine.
_Avoid_: Task, job, feature, action

**Routine**:
The ordered list of Chores configured for one Account.
_Avoid_: Daily, script, macro

**Run**:
One execution of an Account's Routine, from switching to that Account to its summary of each Chore's outcome.
_Avoid_: Session, pass, cycle

**Game day**:
The period between two of Raid's daily resets; each Account gets at most one scheduled Run per Game day.
_Avoid_: Day, reset period

**Account switch**:
Changing which Account the running game is logged into, using logins Plarium Play already remembers.
_Avoid_: Login, relog

## Chores

**Claim playtime rewards**:
Collect the timed playtime gifts whose timers have already finished.

**Claim quests**:
Collect every completed quest reward on the Daily, Weekly, Monthly and Advanced tabs, plus the Daily and Weekly progress chests.
_Avoid_: Daily quests

**Collect inbox**:
Take every inbox item that needs no choice; choice packs are left alone.

**Buy free Market items**:
Take Market items whose price is zero; nothing that costs silver or gems.

**Collect Gem Mine**:
Take the gems accumulated in the Gem Mine.

**Clan check-in**:
Perform the daily Clan check-in.
