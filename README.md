# Evidence Foundry

**AI-Powered Synthetic E-Discovery Evidence Generator**

A <a href="https://www.sevrak.com" target="_blank">Sevrak</a>-maintained fork of <a href="https://github.com/ghanderson77-ops/ReelDiscovery" target="_blank">ReelDiscovery</a> (by <a href="https://www.quikdata.com" target="_blank">QuikData</a>).

We kept what worked well for us, then rebuilt the workflow around litigation realism: guided case issues, a consistent world, a single coherent case storyline with facts, and a mix of responsive and non-responsive documents.

## Built and maintained by

<p align="left"><a href="https://www.sevrak.com" target="_blank"><img src="Resources/sevrak-logo-alt.png" alt="Sevrak" height="64" /></a></p>

**Original project** <a href="https://github.com/ghanderson77-ops/ReelDiscovery" target="_blank">ReelDiscovery</a> by

<p align="left"><a href="https://www.quikdata.com" target="_blank"><img src="Resources/quikdata-logo.png" alt="QuikData" height="48" /></a></p>

---

Evidence Foundry generates realistic, fictional ESI corpora for E-Discovery training, testing, and demonstration purposes.

## Features

### Case and Storyline Modeling

- **Guided case issues** - Select case area, matter type, and issue from the built-in catalog with descriptions
- **World model generation** - Plaintiffs/defendants, org domains, roles, and key people grounded in the case issue
- **Story beats with timelines** - Storyline summary expands into story beats that drive thread counts, email volume and sequential story progression
- **Diverse email thread topics** - Generated email threads are realistic mix of non-responsive and responsive

### Email Generation

- **Storyline-driven content** - Emails follow coherent narratives with beginnings, middles, and conclusions
- **Character voices and signatures** - Personality-driven writing style with enforced signature blocks
- **Realistic threading** - Proper Message-ID, In-Reply-To, and References headers with reply/forward quoting
- **Variable email lengths** - From quick one-line replies to detailed multi-paragraph messages
- **Branching conversations** - CCs, forwards, and side threads that pull in new participants

### Attachments

- **Word Documents (.docx)** - Reports, memos, proposals with organization branding
- **Excel Spreadsheets (.xlsx)** - Data tables, budgets, tracking sheets
- **PowerPoint Presentations (.pptx)** - Slide decks with themed colors and fonts
- **Document versioning** - Realistic document chains with revision labels and evolving content

### AI-Generated Images (DALL-E)

- **Inline images** - Photos embedded directly in HTML email bodies
- **Image attachments** - Photos, screenshots, visual evidence
- **Context-aware** - Images match the storyline and universe

### Voicemails (Text-to-Speech)

- **MP3 audio files** - Realistic voicemail recordings
- **Character voices** - Different TTS voices for each character
- **Natural speech** - Includes conversational elements like "um", pauses

### Calendar Invites

- **Auto-detection** - Finds meeting references in email content
- **.ics files** - Standard calendar format with organizer and attendees
- **Configurable coverage** - Limit what percentage of emails are scanned for invites

### Suggested Search Terms

- **Search terms** - Suggested dtSearch search terms for responsive and hot threads you can use in your testing or demonstrations

### Organization Theming

- **Per-domain branding** - Each organization gets unique colors and fonts
- **AI-selected themes** - Colors match the organization's character (law firms get formal navy, tech startups get vibrant colors)
- **Consistent styling** - HTML emails and documents share branding

## Requirements

- Windows 10/11 (64-bit)
- OpenAI API key with access to:
  - GPT-4o or GPT-4o-mini (for text generation)
  - DALL-E 3 (optional, for image generation)
  - TTS (optional, for voicemails)

## Installation

### Option 1: Download Release

Download the latest `EvidenceFoundry.exe` from the [Releases](../../releases) page. No installation required - just run the executable.

### Option 2: Build from Source

```bash
git clone https://github.com/Sevrak-LLC/evidence-foundry.git
cd EvidenceFoundry
dotnet build
dotnet run
```

## Quick Start

1. **Launch EvidenceFoundry**
2. **Enter your OpenAI API key** and test the connection
3. **Enter a topic** (e.g., "The Office", "Game of Thrones", "Healthcare Startup")
4. **Review generated storylines** - Edit or regenerate as needed
5. **Review characters** - Modify names, roles, organizations
6. **Configure generation settings**:
   - Number of emails
   - Attachment percentages
   - Date range
7. **Generate** - Watch as emails are created
8. **Open output folder** - Import .eml files into your e-discovery tool

## Configuration Options

### Generation Settings

| Setting            | Default | Description                                          |
| ------------------ | ------- | ---------------------------------------------------- |
| Parallel API Calls | 3       | Concurrent requests (higher = faster but more quota) |
| Attachment %       | 20%     | Percentage of emails with document attachments       |

### Attachment Types

- Word Documents (.docx)
- Excel Spreadsheets (.xlsx)
- PowerPoint Presentations (.pptx)

### Optional Features

| Feature          | Default | API Cost          |
| ---------------- | ------- | ----------------- |
| AI Images        | Off     | ~$0.04/image      |
| Voicemails       | Off     | ~$0.015/voicemail |
| Calendar Invites | On      | No extra cost     |

### Model configuration

Default model IDs and pricing live in `Resources/model-configs.json` and are copied alongside the app at build time. When you edit models in the UI, your changes are saved to your user profile and override the defaults for future sessions.

## Output Format

Generated emails are saved as standard `.eml` files that can be imported into:

- Microsoft Outlook
- Relativity
- Nuix
- Concordance
- Most e-discovery platforms

### File Organization

```
output_folder/
├── john.smith@company.com/
│   ├── 20240115_093042_RE_Budget_Meeting.eml
│   └── 20240116_141523_FW_Q4_Report.eml
├── jane.doe@company.com/
│   └── 20240115_102315_Project_Update.eml
└── ...
```

## Disclaimer

All generated content is fictional and created by AI for educational, testing/development, or demonstration purposes only. This tool is intended for:

- E-discovery software training
- Legal technology demonstrations
- Educational purposes
- Testing and development

Do not use generated content to mislead or deceive.

## Tech Stack

- .NET 8.0 / Windows Forms
- OpenAI API (GPT-4, DALL-E, TTS)
- MimeKit (email generation)
- DocumentFormat.OpenXml (Office documents)

## About Sevrak

EvidenceFoundry is brought to you by [Sevrak](https://www.sevrak.com) - an eDiscovery & Legal Tech engineering partner focused on secure, supportable delivery and clear, measurable outcomes.

Sevrak helps teams:

- Build workflow tools, automation, and productized solutions that teams can adopt, support, and extend
- Integrate eDiscovery and legal tech platforms with data sources and downstream systems such as reporting and BI
- Advise on platform architecture and technical decisions to reduce rework and delivery risk

Built for:

- Service providers
- Law firms
- Corporate legal operations
- Legal tech companies

[Learn more at sevrak.com](https://www.sevrak.com)

## License

GPLv3 License - See [LICENSE](LICENSE) for details.

## Contributing

Contributions welcome! Please open an issue to discuss proposed changes before submitting a PR.

## Acknowledgements

- Built with [OpenAI](https://openai.com) APIs
- Email handling by [MimeKit](https://github.com/jstedfast/MimeKit)
- Office documents by [Open XML SDK](https://github.com/OfficeDev/Open-XML-SDK)
- Built and maintained by [Sevrak](https://www.sevrak.com)
- Original project created by [QuikData](https://quikdata.com)
