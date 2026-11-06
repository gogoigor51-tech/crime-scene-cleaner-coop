# Crime Scene Cleaner Co-op

A co-op mod for [Crime Scene Cleaner](https://store.steampowered.com/app/1040200/) (Steam, build 26046462): play the
story jobs together with a Steam friend. BepInEx 6 (IL2CPP) plugin, peer-to-peer over Steam, no dedicated server.

**Status: playable for two players.** Two players finished chapters 1–6 of the story together (0.4.1) and played
Developers' Hell on 0.4.3's test builds. 3–4 players, Nightmare mode and the later chapters are untested.
[Download the latest release](../../releases/latest). Русская версия — ниже.

## What's new in 0.4.3

Fixed after two players' tests on Developers' Hell; all checked by two players.

1. Items at rest react to the action: a thrown or pushed item knocks others over and bounces off them, on both screens.
2. Another player's bucket or lamp can be carried and used; cleaning products (detergents and other modifiers) in
   buckets and power washers are shared.
3. Lamps: switching your lamp no longer switches the other player's; UV lamps light up for their owner first.
4. The elevator moves only the players inside it.
5. Heavy furniture lifted into place by two players at once stays lifted on both screens.
6. Two trash bags merging make one bag, without copies.
7. Blood footprints are shared on every floor.
8. Doors: a door opened or closed by a click, a push or a switch ends the same way on both screens.
9. An item put back on its spot stays fixed there for everyone (it can't be knocked off or fall by itself).
10. Another player's power washer can't be taken (it used to give water without end).
11. The Super Stacker arcade machine is each player's own (it used to open for everyone and break the session); the
    secret passage it opens at 50 000 points opens for everyone.
12. Emails deleted on a computer in a job are deleted for everyone, and the "delete the emails" objective counts for both.
13. Each other's personal flashlight is visible.
14. The job report and the van's body-disposal animation play once, not twice.
15. Slowly sliding items no longer drift apart between the two screens.

## Install

1. Both players: the same Steam build of the game and the **same mod version**.
2. Download `CscCoop-<version>.zip` from Releases and extract it into the game folder
   (`...\steamapps\common\Crime Scene Cleaner`), so `BepInEx\` and `winhttp.dll` sit next to `CrimeCleaner.exe`.
   It contains BepInEx 6 be.791 and the plugin; the first start takes a few minutes while BepInEx generates its files.
3. Back up `CrimeCleaner_Data\Saves` before the first co-op session.
4. Main menu → **КООПЕРАТИВ / Co-op** → host: Host → pick a save slot; friend: Join → slot → the host from the
   Steam friends list. The friend is taken into the host's job automatically. F8 in game: status, ping, leave.
   F9: bug report (see below).

## What is shared

| | Status |
|---|---|
| Connecting from the main menu (host / join, save slot, Steam friends), saves load normally | works |
| Following the host into jobs, leaving together, each player's own report, money and skills | works |
| Laptop jobs and mails in the apartment reach both | works |
| Players see each other: a figure with head/limbs, the tool in hand, a carried body over the shoulder | works |
| Each other's personal flashlight | works (0.4.3) |
| Items: carry, throw, push with tools; another player's item can't be taken from their hands | works |
| Physics near the players: items at rest are knocked over, pushed and bounced off on both screens | works (0.4.3) |
| An item put back on its spot stays fixed there for everyone | works (0.4.3) |
| Bodies: carry, throw (falls on both), into the van; counted once per body | works (double count fixed in 0.4.2) |
| Blood: cleaned stains, new stains, stains cleaned away; objective percent matches | works |
| Trash: trash can pickups, bags (another's bag can be taken), bags into the van, two bags merging into one | works (merging: 0.4.3) |
| Tools: another player's bucket and lamp can be carried and used; water, dirt and cleaning products (modifiers) in buckets and power washers; lamps on/off; UV lamps | works (0.4.3) |
| Another player's power washer can't be taken from its station (only its owner takes the lance) | works (0.4.3) |
| Doors (locked, keys, key cards, one-way; clicks, pushes and switches), drawers, cabinets, lifts and call buttons, power, conveyors, cranes, hacking terminals | works (door clicks: 0.4.3) |
| Elevators (Developers' Hell) move only the players inside | works (0.4.3) |
| Furniture onto holograms, knocked-over furniture stood up (multi-part racks count when complete, as in the game), heavy furniture lifted by two | works |
| Evidence, valuables (on both reports), secrets, cassettes, broken objects and shards | works |
| Story objects (e.g. the attic hatch in the first job), objectives added during a job, objective counters | works |
| Emails deleted on a computer in a job; the "delete the emails" objective | works (0.4.3) |
| Arcade machines (Super Stacker) are each player's own; the reward (the secret passage at 50 000 points) is shared | works (0.4.3) |
| The end-of-job report and the van's body-disposal animation play once | works (0.4.3) |
| Jobs where the two saves differ a little (Villa): only the differing table stops being shared | works (0.4.2) |
| CCTV laptop (Smart House and later): the hack and deleted recordings are shared, the objective counts for both | hack: works; recordings: bot suite only, not tested by players |
| Job restart from the pause menu restarts it for both | 0.4.2, not tested by players |

## Known issues

- **Objective maximums can differ** between players (e.g. furniture 1/21 vs 1/26): the game itself raises the
  maximum when furniture that already stood in place is picked up and put back.
- **Upgraded tools** (e.g. the microfibre mop) may show as the basic tool of the same kind on the other player's
  screen if their game doesn't have that model (0.4.2 fallback, not yet seen by players).
- The walkie-talkie in the Spa job is shared in code but wasn't tested by players.
- Skipping the intro cutscene can leave the pizza box hanging next to the player in the apartment.
- **Kowalski's apartment (the hub) is not synchronized**: what is in it is each player's own; only the players
  themselves see each other there.
- **Progress in a job can go out of sync** when the client's save already has progress in a job and they join the
  host's job that has none.
- **Petting Kowalski's dog**: on the client the petting plays very fast.
- **No more than 3 UV lamps can be lit at once**: a limit of the game; with a fourth on, one of the other player's
  UV lamps stays dark on your screen.
- Not tested: 3–4 players, Nightmare mode, chapters after the sixth, the final mission.

## Bug reports (F9)

F9 saves a screenshot and the game state (job, objectives with counts, held item, position, ping) on **every**
player's machine under one id, then asks "what's wrong?". Reports land in `BepInEx\coop-reports`; send them with
`BepInEx\LogOutput.log`.

## Building and testing

- .NET 6 SDK. The project references the interop assemblies BepInEx generates: point `GameDir` at a game copy that
  has run BepInEx once (`dotnet build src/CscCoop -c Release -p:GameDir=<copy>`); by default it expects
  `local/game-copy`. Never build against your real install; make a copy.
- `src/CoopCore`: the game-free sync core (ownership, poses, world events) with its own tests
  (`dotnet run --project src/CoopCore.Tests`).
- `src/CscCoop`: the plugin. `NOTES.md` is the full engineering log: what each game system does, why each fix was
  needed, and the evidence.
- `tools/run-pair.ps1 -Test <suite> [-Mission 11] [-Mutant <name>]` runs two copies on one PC with bots over
  localhost and checks the result (`tools/<suite>_report.py`). Suites: items, stains, bodies, doors, trash, world2,
  jobs, laptop, cctv, figure, breakhit, bump, toolcarry, footsteps, doorsync, doorclick, products, lamps, elevator,
  fixlift, bagmerge, arcade, flashlight, report, placed, washer, stackreward, mails, vanrace. `-Mission 11` plays it
  on Developers' Hell; `-Mutant` brings back an old bug, and that run must fail. `tools/run-census.ps1` lists every
  component type in every job.

Made with Claude Code (Anthropic) together with the players who tested it. Not affiliated with the game's developers.

---

# Crime Scene Cleaner Co-op (RU)

Кооп-мод для Crime Scene Cleaner (Steam, сборка 26046462): сюжетные заказы вдвоём со Steam-другом.

**Статус: играбельно вдвоём.** Пройдены главы 1–6 вдвоём (0.4.1), Developers' Hell сыгран вдвоём на тестовых
сборках 0.4.3. 3–4 игрока, Nightmare и поздние главы не проверены.

**Что нового в 0.4.3:** предметы рядом с игроками падают и отскакивают у обоих; чужое ведро и лампу можно носить и
ими пользоваться, моющие средства (модификаторы) в вёдрах и мойках общие; своя лампа больше не включает чужую,
УФ-лампы светят у владельца в первую очередь; лифт везёт только тех, кто в кабине; тяжёлая мебель, поднятая вдвоём,
остаётся поднятой у обоих; мешки с мусором сливаются в один без копий; кровавые следы общие на всех полах; двери
открываются и закрываются одинаково у обоих; предмет, поставленный на место, закрепляется у всех; чужую мойку больше
нельзя взять; Super Stacker у каждого свой, а тайный проход за 50 000 очков открывается у всех; удалённые на
компьютере письма удаляются у всех; фонарик друга виден; отчёт и анимация погрузки тела в фургон проигрываются один
раз; медленно сползающие предметы не расходятся. Всё проверено вдвоём.

**Установка:** у обоих одна сборка игры и одна версия мода; архив из Releases распаковать в папку игры (рядом с
`CrimeCleaner.exe`); перед первой игрой скопировать `CrimeCleaner_Data\Saves`. Главное меню → «КООПЕРАТИВ»: хост —
«Хостить» → слот; друг — «Присоединиться» → слот → хост. F8 — статус и пинг, F9 — отчёт о баге (скриншот и
состояние у обоих, папка `BepInEx\coop-reports`).

**Общее:** заказы, предметы и их физика рядом с игроками, тела (без двойного счёта), кровь и следы, мусор и мешки,
инструменты (чужое ведро и лампа, вода, грязь и моющие средства в вёдрах и мойках, лампы и УФ-лампы), двери, ящики,
лифты, питание, конвейеры, терминалы, мебель (в том числе поднятая вдвоём и поставленная на место), улики, ценности,
секреты, кассеты, сюжетные объекты (чердак), счётчики заданий, удалённые письма, награда Super Stacker, взлом ноутбука
камер и удалённые записи. Друг виден с инструментом в руках, фонариком и телом на плече.

**Известные проблемы:** максимум мебели может отличаться (так считает
сама игра); улучшенный инструмент у друга может выглядеть базовым; рация в Spa и перезапуск заказа вживую не
проверены; после пропуска заставки коробка пиццы может висеть рядом с персонажем в квартире; квартира Ковальского
(хаб) не синхронизируется; если в сохранении клиента в заказе уже есть прогресс, а у хоста нет, прогресс расходится;
у клиента поглаживание собаки Ковальского проигрывается очень быстро; одновременно могут светить не больше 3
УФ-ламп (ограничение игры).
