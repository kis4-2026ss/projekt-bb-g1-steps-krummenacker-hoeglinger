# D&D 5e Monster-Datenbank

Diese Datei enthält strukturierte Wertebanken (Statblocks) für häufige Monster und Feinde aus den D&D Basisregeln sowie der Kampagne "Hoard of the Dragon Queen". Die Daten sind hierarchisch formatiert, damit ein RAG-System Entitäten, Attribute und Aktionen sauber trennen kann.

## 1. Kultisten & Handlanger (Hoard of the Dragon Queen)

### Cultist (Kultist)
*Medium Humanoid (jede Rasse), jede nicht-gute Gesinnung*
* **Rüstungsklasse (AC):** 12 (Lederrüstung)
* **Trefferpunkte (HP):** 9 (2d8)
* **Bewegungsrate (Speed):** 30 ft.
* **Attribute:** STR 11 (+0), DEX 12 (+1), CON 10 (+0), INT 10 (+0), WIS 11 (+0), CHA 10 (+0)
* **Fertigkeiten (Skills):** Deception (Täuschen) +2, Religion +2
* **Herausforderungsgrad (CR):** 1/8 (25 XP)
* **Eigenschaften (Traits):**
  * *Dark Devotion (Dunkle Hingabe):* Der Kultist hat Vorteil (Advantage) auf Rettungswürfe gegen Bezauberung (Charmed) und Furcht (Frightened).
* **Aktionen (Actions):**
  * *Scimitar (Krummsäbel):* Nahkampfangriff: +3 zum Treffen, Reichweite 5 ft., ein Ziel. Treffer: 4 (1d6 + 1) Hiebschaden.

### Dragonclaw (Drachenklaue)
*Medium Humanoid (jede Rasse), Neutral Böse*
* **Rüstungsklasse (AC):** 14 (Lederrüstung)
* **Trefferpunkte (HP):** 16 (3d8 + 3)
* **Bewegungsrate (Speed):** 30 ft.
* **Attribute:** STR 9 (-1), DEX 16 (+3), CON 13 (+1), INT 11 (+0), WIS 10 (+0), CHA 12 (+1)
* **Rettungswürfe:** WIS +2
* **Fertigkeiten (Skills):** Deception (Täuschen) +3, Stealth (Heimlichkeit) +5
* **Herausforderungsgrad (CR):** 1 (200 XP)
* **Eigenschaften (Traits):**
  * *Dragon Fanatic:* Vorteil auf Rettungswürfe gegen Charmed und Frightened.
  * *Pack Tactics (Rudeltaktik):* Vorteil auf Angriffswürfe, wenn mindestens ein nicht-kampfunfähiger Verbündeter innerhalb von 5 ft. vom Ziel ist.
* **Aktionen (Actions):**
  * *Multiattack:* Die Drachenklaue macht zwei Angriffe mit dem Scimitar.
  * *Scimitar:* Nahkampfangriff: +5 zum Treffen, Reichweite 5 ft. Treffer: 6 (1d6 + 3) Hiebschaden.

## 2. Häufige Monster (Basic Rules)

### Goblin
*Small Humanoid (Goblinoid), Neutral Böse*
* **Rüstungsklasse (AC):** 15 (Lederrüstung, Schild)
* **Trefferpunkte (HP):** 7 (2d6)
* **Bewegungsrate (Speed):** 30 ft.
* **Attribute:** STR 8 (-1), DEX 14 (+2), CON 10 (+0), INT 10 (+0), WIS 8 (-1), CHA 8 (-1)
* **Fertigkeiten (Skills):** Stealth (Heimlichkeit) +6
* **Sicht & Sinne:** Darkvision (Dunkelsicht) 60 ft., Passive Wahrnehmung 9
* **Herausforderungsgrad (CR):** 1/4 (50 XP)
* **Eigenschaften (Traits):**
  * *Nimble Escape (Flinke Flucht):* Der Goblin kann in jedem seiner Züge die Disengage- (Rückzug) oder Hide-Aktion (Verstecken) als Bonusaktion ausführen.
* **Aktionen (Actions):**
  * *Scimitar:* Nahkampfangriff: +4 zum Treffen, Reichweite 5 ft. Treffer: 5 (1d6 + 2) Hiebschaden.
  * *Shortbow (Kurzbogen):* Fernkampfangriff: +4 zum Treffen, Reichweite 80/320 ft. Treffer: 5 (1d6 + 2) Stichschaden.

### Kobold
*Small Humanoid (Kobold), Rechtschaffen Böse*
* **Rüstungsklasse (AC):** 12
* **Trefferpunkte (HP):** 5 (2d6 - 2)
* **Bewegungsrate (Speed):** 30 ft.
* **Attribute:** STR 7 (-2), DEX 15 (+2), CON 9 (-1), INT 8 (-1), WIS 7 (-2), CHA 8 (-1)
* **Sicht & Sinne:** Darkvision 60 ft., Passive Wahrnehmung 8
* **Herausforderungsgrad (CR):** 1/8 (25 XP)
* **Eigenschaften (Traits):**
  * *Sunlight Sensitivity (Sonnenlichtempfindlichkeit):* Nachteil (Disadvantage) auf Angriffswürfe und Wahrnehmung, wenn der Kobold oder sein Ziel in direktem Sonnenlicht steht.
  * *Pack Tactics (Rudeltaktik):* Vorteil auf Angriffswürfe, wenn ein Verbündeter innerhalb von 5 ft. vom Ziel ist.
* **Aktionen (Actions):**
  * *Dagger (Dolch):* Nahkampf- oder Fernkampfangriff: +4 zum Treffen, Reichweite 5 ft. oder 20/60 ft. Treffer: 4 (1d4 + 2) Stichschaden.
  * *Sling (Schleuder):* Fernkampfangriff: +4 zum Treffen, Reichweite 30/120 ft. Treffer: 4 (1d4 + 2) Wuchtschaden.

### Ogre (Oger)
*Large Giant (Riese), Chaotisch Böse*
* **Rüstungsklasse (AC):** 11 (Hide Armor)
* **Trefferpunkte (HP):** 59 (7d10 + 21)
* **Bewegungsrate (Speed):** 40 ft.
* **Attribute:** STR 19 (+4), DEX 8 (-1), CON 16 (+3), INT 5 (-3), WIS 7 (-2), CHA 7 (-2)
* **Sicht & Sinne:** Darkvision 60 ft., Passive Wahrnehmung 8
* **Herausforderungsgrad (CR):** 2 (450 XP)
* **Aktionen (Actions):**
  * *Greatclub (Großer Knüppel):* Nahkampfangriff: +6 zum Treffen, Reichweite 5 ft. Treffer: 13 (2d8 + 4) Wuchtschaden.
  * *Javelin (Wurfspeer):* Nahkampf- oder Fernkampfangriff: +6 zum Treffen, Reichweite 5 ft. oder 30/120 ft. Treffer: 11 (2d6 + 4) Stichschaden.

## 3. Drachen (Boss-Gegner)

### Young Green Dragon (Junger Grüner Drache)
*Large Dragon, Rechtschaffen Böse*
* **Rüstungsklasse (AC):** 18 (Natürliche Rüstung)
* **Trefferpunkte (HP):** 136 (16d10 + 48)
* **Bewegungsrate (Speed):** 40 ft., Swim (Schwimmen) 40 ft., Fly (Fliegen) 80 ft.
* **Attribute:** STR 19 (+4), DEX 12 (+1), CON 17 (+3), INT 16 (+3), WIS 13 (+1), CHA 15 (+2)
* **Rettungswürfe:** DEX +4, CON +6, WIS +4, CHA +5
* **Fertigkeiten (Skills):** Deception +5, Perception +7, Stealth +4
* **Schadensimmunitäten:** Poison (Gift)
* **Zustandsimmunitäten:** Poisoned (Vergiftet)
* **Sicht & Sinne:** Blindsight (Blindsicht) 30 ft., Darkvision 120 ft., Passive Wahrnehmung 17
* **Sprachen:** Common, Draconic
* **Herausforderungsgrad (CR):** 8 (3.900 XP)
* **Eigenschaften (Traits):**
  * *Amphibious:* Der Drache kann in der Luft und im Wasser atmen.
* **Aktionen (Actions):**
  * *Multiattack:* Der Drache macht drei Angriffe: einen mit dem Biss (Bite) und zwei mit den Klauen (Claws).
  * *Bite (Biss):* Nahkampfangriff: +7 zum Treffen, Reichweite 10 ft. Treffer: 15 (2d10 + 4) Stichschaden plus 7 (2d6) Giftschaden.
  * *Claw (Klaue):* Nahkampfangriff: +7 zum Treffen, Reichweite 5 ft. Treffer: 11 (2d6 + 4) Hiebschaden.
  * *Poison Breath (Giftodem) (Recharge 5-6):* Der Drache speit giftiges Gas in einem 30-Fuß-Kegel. Jede Kreatur in diesem Bereich muss einen DC 14 Konstitutions-Rettungswurf machen. Bei Fehlschlag: 42 (12d6) Giftschaden, bei Erfolg halber Schaden.
