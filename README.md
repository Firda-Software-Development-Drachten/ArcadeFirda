# ArcadeFirda
Een arcade kast software in unity

## Een game op de arcadekast zetten

1. Bouw je game voor Windows.
2. Kopieer de hele buildmap naar `C:\Nonsteamgames\Imported\<NaamVanJeGame>\`. Het menu ziet de nieuwe map na een paar seconden vanzelf.
3. Optioneel, maar wel zo mooi: maak in die map een submap `arcade` met een `game.json`, een `preview.png` en eventueel highscores.

```
C:\Nonsteamgames\Imported\SpaceBlaster\
    SpaceBlaster.exe
    SpaceBlaster_Data\
    arcade\
        game.json          (gegevens over je game)
        preview.png        (16:9, bijvoorbeeld 1280x720)
        highscores.json    (schrijft je game zelf, met ArcadeHighscores.cs)
```

## Wat lever je aan?

Alleen de build is verplicht. Al het andere is optioneel: hoe meer je aanlevert, hoe meer het menu met je game doet.

| Functie in het menu | Wat je aanlevert | Waar | Verplicht |
| --- | --- | --- | --- |
| Game staat in de lijst en start | Windows-build met `.exe` | Eigen map in `C:\Nonsteamgames\Imported` | Ja |
| Game is te spelen op de kast | Volledig te besturen met de gamepad van de kast, fullscreen | In je game | Ja |
| Terug naar het menu | Een manier om af te sluiten met de gamepad, bijvoorbeeld een menuknop Afsluiten die `Application.Quit()` aanroept | In je game | Ja |
| De juiste exe wordt gestart | `exe` (alleen nodig als er meer dan één `.exe` in je map staat) | `arcade\game.json` | Nee |
| Naam in de lijst en in het linkervakje | `title` (anders wordt de mapnaam gebruikt) | `arcade\game.json` | Nee |
| Plaatje in de lijst, in het grote vak en in de slideshow | `preview.png` of `.jpg`, 16:9, bijvoorbeeld 1280x720 | `arcade\` | Nee, anders een gekleurd vlak met de titel |
| Middelste vakje | `playersMin`, `playersMax`, `genre` | `arcade\game.json` | Nee |
| Rechtervakje | `makers`, `year` | `arcade\game.json` | Nee |
| Highscores over het grote plaatje en in de slideshow | `highscores.json`, geschreven door je game met [`ArcadeHighscores.cs`](Assets/Scripts/Shared/ArcadeHighscores.cs) | `arcade\` | Nee |
| Meerdere leaderboards (bijvoorbeeld per gamemode) | `ArcadeHighscores.DefineBoard(...)` en een board-id bij `Submit` | In je game | Nee |
| Scores blijven bewaard als je game opnieuw wordt neergezet | Een vaste `id` | `arcade\game.json` | Nee, anders telt de mapnaam |
| Extra dia's in de slideshow | `screenshots` | `arcade\game.json` + afbeeldingen in `arcade\` | Nee |

De velden `group`, `description` en `controls` mag je ook al invullen; het menu gebruikt ze nog niet, maar dat kan later.

## game.json

Een voorbeeld staat in [`Docs/VoorbeeldGame/arcade/game.json`](Docs/VoorbeeldGame/arcade/game.json). Alle velden zijn optioneel:

| Veld | Betekenis |
| --- | --- |
| `id` | Vaste naam van de game, zonder spaties (bijvoorbeeld `space-blaster`). Wordt gebruikt om highscores te bewaren. |
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

## Highscores

Kopieer [`Assets/Scripts/Shared/ArcadeHighscores.cs`](Assets/Scripts/Shared/ArcadeHighscores.cs) naar je eigen Unity-project. Als een speler klaar is:

```csharp
ArcadeHighscores.Submit("AAA", score);
```

Heeft je game meerdere gamemodes, maak dan bij het opstarten de boards aan en geef het board mee:

```csharp
ArcadeHighscores.DefineBoard("classic", "Classic", "Punten");
ArcadeHighscores.DefineBoard("time-attack", "Time Attack", "Seconden", ArcadeHighscores.SortOrder.LowestFirst);

ArcadeHighscores.Submit("AAA", tijd, "time-attack");
```

Verder handig: `ArcadeHighscores.IsHighscore(score)` om te kijken of je om een naam moet vragen, en `ArcadeHighscores.GetScores()` om de lijst in je eigen game te tonen. Per board worden de beste 10 scores bewaard.

De scores komen in `arcade\highscores.json` naast je `.exe`. In de Unity-editor schrijft het script naar een testbestand in `Application.persistentDataPath`, zodat je kunt testen zonder build. Gebruik je een andere engine, schrijf dan zelf een bestand in dit formaat:

```json
{
  "formatVersion": 1,
  "boards": [
    {
      "id": "classic",
      "title": "Classic",
      "scoreLabel": "Punten",
      "sortOrder": "desc",
      "entries": [
        { "name": "AAA", "score": 12000, "date": "2026-10-28" }
      ]
    }
  ]
}
```

`sortOrder` is `desc` als de hoogste score wint en `asc` als de laagste wint, bijvoorbeeld bij een tijd.

Het menu leest de scores opnieuw zodra je game afgesloten is, en bewaart een kopie. Wordt je game opnieuw neergezet zonder `highscores.json`, dan zet het menu de scores terug. Wil je de scores echt wissen, vraag dan de beheerder van de kast om ook de kopie te verwijderen.

## Instellingen van de kast

De instellingen staan in `arcade-config.json` naast `ArcadeFirda.exe` (in de Unity-editor: in de hoofdmap van het project). Het bestand wordt bij de eerste start aangemaakt en staat niet in Git, omdat het wachtwoord erin staat. Zie [`arcade-config.example.json`](arcade-config.example.json).

| Instelling | Betekenis |
| --- | --- |
| `gamesRoot` | Map met de games. |
| `desktopPassword` | Wachtwoord voor de knop Desktop. Leeg = de knop werkt niet. |
| `idleSeconds` | Seconden zonder input voordat de slideshow start. |
| `shutdownTime` | Tijdstip (`HH:mm`) waarop de pc automatisch uitgaat. Leeg = nooit. |

De kopieën van de highscores staan in `%USERPROFILE%\AppData\LocalLow\Firda\Arcadekast Firda\highscore-backup`.
