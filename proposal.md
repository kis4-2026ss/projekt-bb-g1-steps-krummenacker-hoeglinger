# Proposal: Discord Dungeon Master Bot (KI & Systemarchitektur)

## 1. Projektübersicht
Entwicklung eines Discord-Bots in C# (.NET 8+), der als Dungeon Master (DM) für Dungeons & Dragons (oder ähnliche Pen & Paper Systeme) fungiert. Der Bot übernimmt ausschließlich die narrative Führung der Geschichte und steuert Feinde/NPCs im Kampf. Er verlässt sich auf die Spieler zur Verwaltung von Charakterwerten, Lebenspunkten und Inventaren.

## 2. Team
- Steps
- Krummenacker
- Höglinger

## 3. Tech-Stack
- **Programmiersprache:** C# (.NET 8+)
- **Discord-Anbindung:** Discord.Net
- **KI-Backend:** Ollama (lokal gehostet auf AMD 7900XT), Kommunikation via OllamaSharp. Modellauswahl: z.B. llama3.
- **Datenbank:** SQLite via Entity Framework Core (EF Core)

## 4. Kern-Architektur & Komponenten
### A. Discord-Bot Layer
- Lauscht in aktiven Kampagnen-Kanälen auf Nachrichten.
- Slash Commands: `/start_campaign`, `/stop_campaign`, `/summarize`.

### B. KI & Prompting Layer (Ollama)
- Verwendet Prompt-Engineering, um die narrative DM-Rolle durchzusetzen.
- Restriktionen: Keine Übernahme von Spieleraktionen, keine automatische Verwaltung von HP/Werten.

### C. Memory & State Management (Dual-Layer)
- **Kurzzeitgedächtnis (RAM):** Speichert die letzten X (z.B. 10) Nachrichten im Context Window für reaktives Rollenspiel.
- **Langzeitgedächtnis (SQLite):** Führt regelmäßige Zusammenfassungen (Summarization Loop) der ältesten Nachrichten im Kurzzeitgedächtnis durch. Extrahiert "Story Beats", die in einer lokalen SQLite-Datenbank gespeichert werden. Diese werden unsichtbar als Kontext in künftige System-Prompts injiziert.
