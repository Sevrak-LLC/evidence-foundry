# ReelDiscovery

**AI-Powered E-Discovery Email Dataset Generator**

*A free tool from [QuikData](https://quikdata.com) — Scalable On-Prem, SaaS, and Hybrid E-Discovery solutions for law firms, service providers, and corporate/government. AI Powered.*

---

ReelDiscovery generates realistic corporate email datasets for e-discovery training, testing, and demonstration purposes. Using OpenAI's GPT models, it creates authentic-feeling email threads based on movies, TV shows, books, or custom business scenarios.

## Features

### Email Generation
- **Storyline-driven content** - Emails follow coherent narratives with beginnings, middles, and conclusions
- **Character personalities** - Each character has a unique voice, writing style, and email signature
- **Realistic threading** - Proper email threading with Message-ID, In-Reply-To, and References headers
- **Variable email lengths** - From quick one-line replies to detailed multi-paragraph messages
- **Branching conversations** - Side threads, forwards, and CC'd participants

### Attachments
- **Word Documents (.docx)** - Reports, memos, proposals with organization branding
- **Excel Spreadsheets (.xlsx)** - Data tables, budgets, tracking sheets
- **PowerPoint Presentations (.pptx)** - Slide decks with themed colors and fonts
- **Document versioning** - Realistic version chains (v1, v2_revised, v3_FINAL, etc.)

### AI-Generated Images (DALL-E)
- **Inline images** - Photos embedded directly in email body
- **Image attachments** - Photos, screenshots, visual evidence
- **Context-aware** - Images match the storyline and universe

### Voicemails (Text-to-Speech)
- **MP3 audio files** - Realistic voicemail recordings
- **Character voices** - Different TTS voices for each character
- **Natural speech** - Includes conversational elements like "um", pauses

### Calendar Invites
- **Auto-detection** - Finds meeting references in email content
- **.ics files** - Standard calendar format compatible with Outlook, Gmail, etc.
- **Attendee lists** - Pulls participants from email recipients

### Organization Theming
- **Per-domain branding** - Each organization gets unique colors and fonts
- **AI-selected themes** - Colors match the organization's character (law firms get formal navy, tech startups get vibrant colors)
- **Consistent styling** - Documents from the same organization share branding

## Requirements

- Windows 10/11 (64-bit)
- OpenAI API key with access to:
  - GPT-4o or GPT-4o-mini (for text generation)
  - DALL-E 3 (optional, for image generation)
  - TTS (optional, for voicemails)

## Installation

### Option 1: Download Release
Download the latest `ReelDiscovery.exe` from the [Releases](../../releases) page. No installation required - just run the executable.

### Option 2: Build from Source
```bash
git clone https://github.com/yourusername/ReelDiscovery.git
cd ReelDiscovery
dotnet build
dotnet run
```

## Quick Start

1. **Launch ReelDiscovery**
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
| Setting | Default | Description |
|---------|---------|-------------|
| Email Count | 50 | Total emails to generate |
| Parallel API Calls | 3 | Concurrent requests (higher = faster but more quota) |
| Attachment % | 20% | Percentage of emails with document attachments |

### Attachment Types
- Word Documents (.docx)
- Excel Spreadsheets (.xlsx)
- PowerPoint Presentations (.pptx)

### Optional Features
| Feature | Default | API Cost |
|---------|---------|----------|
| AI Images | Off | ~$0.04/image |
| Voicemails | Off | ~$0.015/voicemail |
| Calendar Invites | On | No extra cost |

## API Cost Estimates

Costs vary based on the model selected and features enabled:

| Model | ~Cost per 50 emails |
|-------|---------------------|
| GPT-4o-mini | $0.10 - $0.30 |
| GPT-4o | $0.50 - $1.50 |
| GPT-4-turbo | $0.80 - $2.00 |

Additional costs:
- DALL-E images: ~$0.04 each
- TTS voicemails: ~$0.015 each

The application tracks token usage and displays estimated costs in the completion summary.

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

## Example Topics

ReelDiscovery works great with:

**TV Shows & Movies**
- "The Office" - Dunder Mifflin corporate drama
- "Game of Thrones" - Medieval politics and intrigue
- "Succession" - Family business conflicts
- "Silicon Valley" - Tech startup chaos

**Business Scenarios**
- "Healthcare Company Merger"
- "Tech Startup Funding Round"
- "Law Firm Partnership Dispute"
- "Manufacturing Quality Issues"

## Disclaimer

All generated content is fictional and created by AI for demonstration purposes only. Generated emails include a disclaimer banner. This tool is intended for:
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

## About QuikData

ReelDiscovery is brought to you by **[QuikData](https://quikdata.com)** — a leading provider of AI-powered e-discovery solutions.

**QuikData offers:**
- **On-Premises** - Full control with deployment in your own data center
- **SaaS** - Cloud-hosted solution with no infrastructure to manage
- **Hybrid** - Flexible deployment combining on-prem processing with cloud review

**Built for:**
- Law Firms
- Legal Service Providers
- Corporate Legal Departments
- Government Agencies

[Learn more at quikdata.com](https://quikdata.com)

## License

MIT License - See [LICENSE](LICENSE) for details.

## Contributing

Contributions welcome! Please open an issue to discuss proposed changes before submitting a PR.

## Acknowledgments

- Built with [OpenAI](https://openai.com) APIs
- Email handling by [MimeKit](https://github.com/jstedfast/MimeKit)
- Office documents by [Open XML SDK](https://github.com/OfficeDev/Open-XML-SDK)
- Created by [QuikData](https://quikdata.com)
