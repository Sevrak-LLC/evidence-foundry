using EvidenceFoundry.Models;

namespace EvidenceFoundry.Helpers;

public static class DeterministicPersonalityHelper
{
    private sealed record Persona(string Personality, string CommunicationStyle);

    private static readonly Persona[] Personas =
    {
        new(
            "Upbeat and collaborative, they look for common ground quickly. They keep morale steady even under pressure. They assume good intent and want practical solutions.",
            "Writes medium, informal emails with a warm tone and occasional slang."),
        new(
            "Friendly but structured, they like clear agendas and next steps. They are patient when explaining details. They take pride in being reliable and responsive.",
            "Writes medium, formal emails with a polite tone and no slang."),
        new(
            "Analytical and measured, they focus on facts over feelings. They prefer precision and avoid speculation. They can seem detached but are fair.",
            "Writes short, formal emails that are concise and use no slang."),
        new(
            "Pragmatic and steady, they keep discussions grounded in constraints. They dislike drama and stay even-tempered. They adapt quickly when priorities shift.",
            "Writes short, formal emails with a straightforward tone and no slang."),
        new(
            "Skeptical by default, they question assumptions and push back on weak claims. They can be blunt when timelines slip. They still want the work to succeed.",
            "Writes short, very formal emails that are direct and use no slang."),
        new(
            "Impatient and easily frustrated, they dislike ambiguity. They can be sharp when repeating themselves. They value control over consensus.",
            "Writes short, very formal emails with a terse tone and no slang."),
        new(
            "Energetic and optimistic, they enjoy brainstorming. They celebrate wins and encourage the team. They can overlook small risks when excited.",
            "Writes long, informal emails that are lively and include occasional slang."),
        new(
            "Cautious and risk-aware, they look for edge cases. They prefer approvals before acting. They are slow to trust new vendors.",
            "Writes long, formal emails with detailed context and no slang."),
        new(
            "Diplomatic and tactful, they aim to keep relationships smooth. They listen carefully before responding. They avoid taking sides in conflicts.",
            "Writes medium, formal emails with a courteous tone and no slang."),
        new(
            "Candid and plain-spoken, they value speed over polish. They are comfortable disagreeing in the open. They move on quickly after decisions.",
            "Writes medium, very informal emails that are blunt and use slang occasionally."),
        new(
            "Calm and patient, they take time to mentor others. They prioritize clarity and follow-through. They avoid conflict and prefer steady progress.",
            "Writes long, formal emails that are careful and use no slang."),
        new(
            "Assertive and competitive, they push for fast outcomes. They dislike delays and loose commitments. They are confident even when challenged.",
            "Writes medium, formal emails with a firm tone and no slang."),
        new(
            "Radiates encouragement and makes people feel capable. They celebrate progress, share credit, and keep momentum without forcing it. They’re optimistic but still practical.",
            "Writes medium, informal emails with a warm tone, upbeat phrasing, and occasional light slang."),
        new(
            "Service-oriented and dependable, they quietly remove blockers for others. They follow through without reminders and communicate early when something slips. They earn trust through consistency.",
            "Writes medium, formal emails with a calm, polite tone and no slang."),
        new(
            "Thoughtful and empathetic, they notice unspoken concerns and address them gently. They de-escalate conflict and help people save face. They value long-term relationships over short-term wins.",
            "Writes long, semi-formal emails with considerate wording, clear context, and no slang."),
        new(
            "Clear-headed and uplifting under stress, they keep teams steady during incidents. They focus on solutions, not blame, and keep everyone aligned. Their confidence is reassuring, not pushy.",
            "Writes short, formal emails that are crisp, reassuring, and use no slang."),
        new(
            "Process-driven and methodical, they prefer checklists and predictable routines. They dislike surprises and want ownership to be explicit. They’re not warm or cold—just systematic.",
            "Writes long, formal emails with structured sections, bullets, and no slang."),
        new(
            "Curious and experiment-minded, they’d rather test than argue. They propose small trials, measure results, and iterate quickly. They can seem restless if things move slowly.",
            "Writes medium, informal emails with lots of questions, concise suggestions, and minimal slang."),
        new(
            "Reserved and observant, they speak up only when they have something solid. They track details quietly and surface issues at the right moment. They prefer written updates to group debates.",
            "Writes short, formal emails that are understated, precise, and use no slang."),
        new(
            "Strategic and outcome-focused, they frame tasks in terms of impact and tradeoffs. They dislike busywork and push for decisions. They can ignore minor concerns if they don’t affect the goal.",
            "Writes medium, semi-formal emails with clear asks, timelines, and no slang."),
        new(
            "Practical and hands-on, they jump into implementation quickly. They’re comfortable learning by doing and adjusting mid-stream. They sometimes forget to narrate progress until asked.",
            "Writes short, informal emails that are direct, action-oriented, and include rare slang."),
        new(
            "Diplomatically blunt, they say what they mean but try not to embarrass anyone. They correct misunderstandings early and prefer clarity over comfort. They move on fast after resolving conflict.",
            "Writes medium, formal emails that are straightforward, tactful, and use no slang."),
        new(
            "Metrics-oriented and skeptical of vague claims, they want evidence and definitions. They’re willing to change direction if data suggests it. They can be slow to commit without benchmarks.",
            "Writes long, formal emails with figures, assumptions, and no slang."),
        new(
            "Big-picture and pattern-seeking, they connect threads across teams and time. They like frameworks and mental models. They can drift into abstraction if not grounded by specifics.",
            "Writes long, semi-formal emails with conceptual framing and no slang."),
        new(
            "Boundary-conscious and principled, they hold standards consistently. They don’t escalate emotionally, but they do escalate formally when needed. They’re fair, even when firm.",
            "Writes short, very formal emails with a firm tone and no slang."),
        new(
            "Facilitative and synthesis-focused, they collect viewpoints and produce a single coherent plan. They summarize disagreements neutrally and propose a decision point. They’re comfortable making the final call if asked.",
            "Writes medium, formal emails with summaries, options, and no slang."),
        new(
            "Risk-aware but not alarmist, they look for edge cases and failure modes. They prefer mitigations and clear rollback paths. They don’t block progress unless the risk is concrete.",
            "Writes long, formal emails that include contingencies and no slang."),
        new(
            "Minimalist and scope-trimming, they constantly reduce complexity. They prioritize the simplest viable approach and push back on ‘nice-to-haves.’ They can seem indifferent to polish if it doesn’t matter.",
            "Writes very short, formal emails that are concise, list-heavy, and use no slang."),
        new(
            "Defensive and status-sensitive, they interpret feedback as criticism. They protect their turf and can become combative when challenged. They may prioritize being right over being effective.",
            "Writes medium, formal emails that are stiff, carefully worded, and contain subtle defensiveness with no slang."),
        new(
            "Cynical and dismissive, they assume initiatives will fail and say so openly. They focus on flaws more than fixes and can drain energy from a room. They still do the work, but without enthusiasm.",
            "Writes short, very formal emails that are cold, skeptical, and use no slang."),
        new(
            "Micromanaging and controlling, they struggle to delegate and re-check everything. They demand constant updates and rewrite others’ work. They value predictability over autonomy.",
            "Writes long, formal emails with excessive detail, many directives, and no slang."),
        new(
            "Passive-aggressive and indirect, they avoid saying ‘no’ but obstruct through delay and ambiguity. They use polite phrasing to mask frustration. They rarely surface issues until they’re problems.",
            "Writes medium, formal emails that are polished, vague in commitments, and include pointed subtext with no slang."),
        new(
            "Generous with time and attention, they make others feel heard. They give constructive feedback kindly and follow up to ensure people aren’t stuck. Their optimism is steady, not naive.",
            "Writes long, semi-formal emails with supportive language, clear context, and no slang."),
        new(
            "Cheerfully decisive, they turn uncertainty into next steps without steamrolling. They keep teams aligned with simple priorities and quick check-ins. They’re upbeat even when plans change.",
            "Writes medium, informal emails with upbeat phrasing and occasional light slang."),
        new(
            "Humble and credit-forward, they highlight others’ wins and downplay their own. They ask good questions and admit gaps quickly. They’re easy to collaborate with because ego stays out of it.",
            "Writes medium, formal emails with a polite tone, gratitude, and no slang."),
        new(
            "Calmly motivating under deadlines, they keep focus on what matters and protect morale. They reduce panic by clarifying roles and constraints. They praise effort and reinforce progress.",
            "Writes short, formal emails that are reassuring, focused, and use no slang."),
        new(
            "Deliberate and slow-to-speak, they think before responding and dislike being rushed. They prefer written proposals over verbal improvisation. Once convinced, they commit fully.",
            "Writes medium, formal emails with careful wording, minimal flourish, and no slang."),
        new(
            "Conceptual and systems-minded, they map dependencies and identify leverage points. They enjoy diagrams, architecture, and abstractions. They can lose patience with purely tactical chatter.",
            "Writes long, formal emails with structured reasoning, diagrams described in text, and no slang."),
        new(
            "Straightforward and time-conscious, they keep meetings short and push decisions forward. They don’t sugarcoat, but they aren’t hostile either. They value clarity over harmony.",
            "Writes short, formal emails that are direct, action-oriented, and use no slang."),
        new(
            "Collaborative but reserved, they prefer small groups and async discussion. They contribute thoughtful notes rather than real-time debate. They’re steady in execution and low-drama.",
            "Writes medium, semi-formal emails with clear bullet points and no slang."),
        new(
            "Evidence-seeking and precise, they define terms and ask for examples. They dislike hand-wavy statements and want reproducible steps. They’re fair, but hard to persuade without specifics.",
            "Writes long, formal emails with explicit assumptions and no slang."),
        new(
            "Deadline-driven and pragmatic, they choose ‘good enough’ when the clock demands it. They communicate tradeoffs plainly and keep scope realistic. They can appear blunt when time is tight.",
            "Writes medium, formal emails with firm deadlines, clear asks, and no slang."),
        new(
            "Curatorial and organized, they maintain shared docs, naming conventions, and tidy backlogs. They enjoy categorizing and reducing clutter. They can be annoyed by messy threads but stay polite.",
            "Writes long, formal emails with headings, links, and no slang."),
        new(
            "Mentally agile and improvisational, they adapt quickly in live discussions. They’re comfortable making provisional decisions and revising later. They sometimes skip documentation unless prompted.",
            "Writes short, informal emails that are conversational and include rare slang."),
        new(
            "Stakeholder-aware and political-neutral, they anticipate reactions and align expectations early. They manage optics without being manipulative. They’re good at framing tradeoffs for different audiences.",
            "Writes medium, formal emails with diplomatic phrasing and no slang."),
        new(
            "Detail-sensitive and cautious, they prefer slow changes and incremental rollouts. They ask for rollback plans and monitoring. They don’t block progress, but they do insist on guardrails.",
            "Writes long, formal emails with checklists, mitigations, and no slang."),
        new(
            "Exploration-friendly and idea-collecting, they keep a backlog of possibilities. They enjoy brainstorming but are willing to park ideas when priorities demand it. They can meander unless time-boxed.",
            "Writes long, informal emails with lots of options, tangents, and occasional light slang."),
        new(
            "Consistency-focused and routine-loving, they thrive on stable schedules and predictable workflows. They prefer incremental improvements over big reorganizations. They don’t seek attention; they just keep things running.",
            "Writes medium, formal emails with steady tone, clear status, and no slang."),
        new(
            "Aloof and unresponsive, they disappear during coordination and reappear with last-minute demands. They treat communication as optional and expect others to fill in gaps. They can cause churn without noticing.",
            "Writes very short, formal emails that are minimal, late, and use no slang."),
        new(
            "Overconfident and dismissive, they wave off concerns as ‘non-issues’ and push ahead. They assume their judgment is enough and can ignore feedback. They’re productive, but risky to rely on.",
            "Writes short, semi-formal emails with a blunt tone and occasional snark without slang."),
        new(
            "Nitpicky and fault-finding, they fixate on minor errors and derail progress. They correct people publicly and keep score. They rarely acknowledge what’s working.",
            "Writes long, formal emails packed with critiques, annotations, and no slang."),
        new(
            "Manipulative and credit-hungry, they position themselves as the owner of others’ work. They selectively share information to stay central. They praise in public but undermine in private.",
            "Writes medium, formal emails that are polished, self-promoting, and carefully phrased with no slang.")
    };

    public static void EnsurePersonality(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);

        if (!string.IsNullOrWhiteSpace(character.Personality)
            && !string.IsNullOrWhiteSpace(character.CommunicationStyle))
            return;

        var persona = ResolvePersona(character);

        if (string.IsNullOrWhiteSpace(character.Personality))
            character.Personality = persona.Personality;

        if (string.IsNullOrWhiteSpace(character.CommunicationStyle))
            character.CommunicationStyle = persona.CommunicationStyle;
    }

    public static (string Personality, string CommunicationStyle) ResolvePersona(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);

        var seed = DeterministicSeedHelper.CreateSeed(
            "character-persona",
            character.Id != Guid.Empty ? character.Id.ToString("N") : null,
            character.Email?.ToLowerInvariant(),
            character.FirstName,
            character.LastName);
        var persona = Personas[seed % Personas.Length];
        return (persona.Personality, persona.CommunicationStyle);
    }
}
