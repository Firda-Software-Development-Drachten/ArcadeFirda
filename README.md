# ArcadeFirda
Een arcade kast software in unity

## Een game op de arcadekast zetten

1. Bouw je game voor Windows.
2. Kopieer de hele buildmap naar `C:\Nonsteamgames\Imported\<NaamVanJeGame>\`. Het menu ziet de nieuwe map na een paar seconden vanzelf.
3. Optioneel, maar wel zo mooi: maak in die map een submap `arcade` met een `game.json` en een `preview.png`.

```
C:\Nonsteamgames\Imported\SpaceBlaster\
    SpaceBlaster.exe
    SpaceBlaster_Data\
    arcade\
        game.json
        preview.png        (16:9, bijvoorbeeld 1280x720)
```

Een voorbeeld van `game.json` staat in [`Docs/VoorbeeldGame/arcade/game.json`](Docs/VoorbeeldGame/arcade/game.json). Alle velden zijn optioneel:

| Veld | Betekenis |
| --- | --- |
| `id` | Vaste naam van de game, zonder spaties (bijvoorbeeld `space-blaster`). Wordt later gebruikt voor highscores. |
| `title` | Naam in het menu. Zonder titel wordt de mapnaam gebruikt. |
| `exe` | Welke `.exe` gestart moet worden, als er meer dan één in de map staat. |
| `makers` | Lijst met namen van de makers. |
| `year` | Jaar waarin de game gemaakt is. |
| `group` | Klas of groep. |
| `playersMin`, `playersMax` | Aantal spelers. |
| `genre` | Bijvoorbeeld Platformer, Shooter, Puzzel. |
| `description` | Korte omschrijving. |
| `controls` | Besturing, bijvoorbeeld `Joystick: bewegen, knop A: springen`. |
| `preview` | Bestandsnaam van de preview in de map `arcade`. Standaard `preview.png`. |
| `screenshots` | Extra afbeeldingen voor de slideshow. |

Zonder `arcade`-map werkt je game ook, dan krijgt hij een gekleurd vlak met de titel als preview.

## Instellingen van de kast

De instellingen staan in `arcade-config.json` naast `ArcadeFirda.exe` (in de Unity-editor: in de hoofdmap van het project). Het bestand wordt bij de eerste start aangemaakt en staat niet in Git, omdat het wachtwoord erin staat. Zie [`arcade-config.example.json`](arcade-config.example.json).

| Instelling | Betekenis |
| --- | --- |
| `gamesRoot` | Map met de games. |
| `desktopPassword` | Wachtwoord voor de knop Desktop. Leeg = de knop werkt niet. |
| `idleSeconds` | Seconden zonder input voordat de slideshow start. |
| `shutdownTime` | Tijdstip (`HH:mm`) waarop de pc automatisch uitgaat. Leeg = nooit. |
