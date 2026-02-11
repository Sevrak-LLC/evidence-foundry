using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Services;

public class EmailThreadGenerator
{
    private const double InternalThreadOdds = 0.7;

    internal void AssignThreadParticipants(
        EmailThread thread,
        IReadOnlyList<Organization> organizations,
        Random rng)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(organizations);
        ArgumentNullException.ThrowIfNull(rng);

        var availableOrganizations = organizations
            .Where(o => o != null)
            .Where(o => GetOrganizationCharacters(o).Count > 0)
            .ToList();

        thread.OrganizationParticipants.Clear();
        thread.CharacterParticipants.Clear();
        thread.RoleParticipants.Clear();

        if (availableOrganizations.Count == 0)
            return;

        var requiresKey = thread.Relevance == EmailThread.ThreadRelevance.Responsive || thread.IsHot;

        if (thread.Scope == EmailThreadScope.External)
        {
            var selectedOrgs = SelectExternalOrganizations(availableOrganizations, requiresKey, rng);
            thread.OrganizationParticipants = selectedOrgs;
            thread.CharacterParticipants = SelectExternalCharacters(selectedOrgs, requiresKey, rng);
        }
        else
        {
            var selectedOrg = SelectInternalOrganization(availableOrganizations, requiresKey, rng);
            if (selectedOrg != null)
            {
                thread.OrganizationParticipants = new List<Organization> { selectedOrg };
                thread.CharacterParticipants = SelectInternalCharacters(selectedOrg, requiresKey, rng);
            }
        }

        thread.RoleParticipants = BuildRoleParticipants(thread.CharacterParticipants, organizations);
    }

    internal void PlanEmailThreadsForBeats(
        IReadOnlyList<StoryBeat> beats,
        int keyRoleCount,
        Random rng)
    {
        ArgumentNullException.ThrowIfNull(beats);
        ArgumentNullException.ThrowIfNull(rng);
        if (keyRoleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(keyRoleCount), "Key role count must be positive.");

        foreach (var beat in beats)
        {
            beat.EmailCount = DateHelper.CalculateEmailCountForRange(
                beat.StartDate.Date,
                beat.EndDate.Date,
                keyRoleCount,
                rng);

            beat.Threads = CreateThreads(beat, rng);
        }

        EnsureThreadRelevanceCoverage(beats, rng);
    }

    internal (double responsive, double hot) GetThreadOdds(int emailCount)
    {
        if (emailCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(emailCount), "Thread email count must be positive.");

        const double qR = 0.0794979079497908;
        const double rHi = 0.12;
        const double rLo = 0.0005;

        const double qH = 0.06542056074766354;
        const double hHi = 0.015;
        const double hLo = 0.00002;

        var responsive = qR * (1 - Math.Pow(1 - rHi, emailCount))
                         + (1 - qR) * (1 - Math.Pow(1 - rLo, emailCount));
        var hot = qH * (1 - Math.Pow(1 - hHi, emailCount))
                  + (1 - qH) * (1 - Math.Pow(1 - hLo, emailCount));

        return (responsive, hot);
    }

    internal (EmailThread.ThreadRelevance relevance, bool isHot) EvaluateThreadRelevance(
        int emailCount,
        double responsiveRoll,
        double hotRoll)
    {
        if (responsiveRoll is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(responsiveRoll), "Roll must be between 0.0 and 1.0.");
        if (hotRoll is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(hotRoll), "Roll must be between 0.0 and 1.0.");

        var (responsiveOdds, hotOdds) = GetThreadOdds(emailCount);
        var isHot = hotRoll <= hotOdds;
        var isResponsive = responsiveRoll <= responsiveOdds || isHot;

        return (isResponsive ? EmailThread.ThreadRelevance.Responsive : EmailThread.ThreadRelevance.NonResponsive, isHot);
    }

    internal void EnsurePlaceholderMessages(EmailThread thread, int emailCount)
    {
        ArgumentNullException.ThrowIfNull(thread);
        if (emailCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(emailCount), "Thread email count must be positive.");

        if (thread.EmailMessages.Count == emailCount)
            return;

        if (thread.EmailMessages.Count > 0)
            throw new InvalidOperationException($"Thread placeholder count ({thread.EmailMessages.Count}) does not match planned email count ({emailCount}).");

        thread.EmailMessages = new List<EmailMessage>(emailCount);
        for (var i = 0; i < emailCount; i++)
        {
            thread.EmailMessages.Add(new EmailMessage
            {
                EmailThreadId = thread.Id,
                StoryBeatId = thread.StoryBeatId,
                StorylineId = thread.StorylineId,
                SequenceInThread = i
            });
        }
    }

    internal void ResetThreadForRetry(EmailThread thread, int emailCount)
    {
        ArgumentNullException.ThrowIfNull(thread);
        if (emailCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(emailCount), "Thread email count must be positive.");

        thread.EmailMessages = new List<EmailMessage>(emailCount);
        for (var i = 0; i < emailCount; i++)
        {
            thread.EmailMessages.Add(new EmailMessage
            {
                EmailThreadId = thread.Id,
                StoryBeatId = thread.StoryBeatId,
                StorylineId = thread.StorylineId,
                SequenceInThread = i
            });
        }
    }

    private List<EmailThread> CreateThreads(StoryBeat beat, Random rng)
    {
        ArgumentNullException.ThrowIfNull(beat);

        if (beat.StorylineId == Guid.Empty)
            throw new InvalidOperationException($"Story beat '{beat.Name}' is missing a StorylineId.");

        var emailCount = beat.EmailCount;
        if (emailCount < 0)
            throw new ArgumentOutOfRangeException(nameof(emailCount), "Email count must be non-negative.");

        if (emailCount == 0)
            return new List<EmailThread>();

        var sizes = DateHelper.BuildThreadSizePlan(emailCount, rng);
        var threads = new List<EmailThread>(sizes.Count);

        foreach (var size in sizes)
        {
            var thread = new EmailThread
            {
                StoryBeatId = beat.Id,
                StorylineId = beat.StorylineId
            };

            thread.Scope = rng.NextDouble() < InternalThreadOdds
                ? EmailThreadScope.Internal
                : EmailThreadScope.External;

            for (var i = 0; i < size; i++)
            {
                thread.EmailMessages.Add(new EmailMessage
                {
                    EmailThreadId = thread.Id,
                    StoryBeatId = thread.StoryBeatId,
                    StorylineId = thread.StorylineId,
                    SequenceInThread = i
                });
            }

            var (relevance, isHot) = EvaluateThreadRelevance(
                thread.EmailMessages.Count,
                rng.NextDouble(),
                rng.NextDouble());

            thread.Relevance = relevance;
            thread.IsHot = isHot;

            threads.Add(thread);
        }

        return threads;
    }

    private void EnsureThreadRelevanceCoverage(IReadOnlyList<StoryBeat> beats, Random rng)
    {
        var beatsWithThreads = beats
            .Where(b => b.Threads.Count > 0)
            .ToList();

        if (beatsWithThreads.Count == 0)
            return;

        foreach (var beat in beatsWithThreads)
        {
            if (!beat.Threads.Any(t => t.IsHot || t.Relevance == EmailThread.ThreadRelevance.Responsive))
            {
                var promoted = SelectThreadForPromotion(beat.Threads, rng);
                promoted.Relevance = EmailThread.ThreadRelevance.Responsive;
            }
        }

        if (!beatsWithThreads.SelectMany(b => b.Threads).Any(t => t.IsHot))
        {
            var middleIndex = beatsWithThreads.Count / 2;
            var beat = beatsWithThreads[middleIndex];
            var promoted = SelectThreadForPromotion(beat.Threads, rng);
            promoted.IsHot = true;
            promoted.Relevance = EmailThread.ThreadRelevance.Responsive;
        }
    }

    private EmailThread SelectThreadForPromotion(IReadOnlyList<EmailThread> threads, Random rng)
    {
        if (threads.Count == 1)
            return threads[0];

        return threads[rng.Next(threads.Count)];
    }

    private static Organization? SelectInternalOrganization(
        IReadOnlyList<Organization> organizations,
        bool requiresKey,
        Random rng)
    {
        if (organizations.Count == 0)
            return null;

        if (requiresKey)
        {
            var keyOrgs = organizations.Where(OrganizationHasKeyCharacter).ToList();
            if (keyOrgs.Count > 0)
                return keyOrgs[rng.Next(keyOrgs.Count)];
        }

        return organizations[rng.Next(organizations.Count)];
    }

    private static List<Organization> SelectExternalOrganizations(
        IReadOnlyList<Organization> organizations,
        bool requiresKey,
        Random rng)
    {
        if (organizations.Count == 0)
            return new List<Organization>();

        if (organizations.Count <= 2)
        {
            return organizations
                .OrderBy(_ => rng.Next())
                .Take(organizations.Count)
                .ToList();
        }

        var selected = PickRandomDistinct(organizations.ToList(), 2, rng);
        if (requiresKey && !selected.Any(OrganizationHasKeyCharacter))
        {
            var keyOrgs = organizations
                .Where(OrganizationHasKeyCharacter)
                .Where(o => selected.All(s => s.Id != o.Id))
                .ToList();

            if (keyOrgs.Count > 0)
            {
                var replacement = keyOrgs[rng.Next(keyOrgs.Count)];
                var replaceIndex = rng.Next(selected.Count);
                selected[replaceIndex] = replacement;
            }
        }

        return selected;
    }

    private static List<Character> SelectInternalCharacters(
        Organization organization,
        bool requiresKey,
        Random rng)
    {
        var candidates = GetOrganizationCharacters(organization);
        if (candidates.Count == 0)
            return new List<Character>();

        var targetCount = rng.Next(2, 6);
        targetCount = Math.Clamp(targetCount, 1, candidates.Count);

        var selected = new List<Character>();

        if (requiresKey)
        {
            var keyCandidates = candidates.Where(c => c.IsKeyCharacter).ToList();
            if (keyCandidates.Count > 0)
            {
                selected.Add(keyCandidates[rng.Next(keyCandidates.Count)]);
            }
        }

        var remaining = candidates.Where(c => selected.All(s => s.Id != c.Id)).ToList();
        var needed = targetCount - selected.Count;
        if (needed > 0)
        {
            selected.AddRange(PickRandomDistinct(remaining, Math.Min(needed, remaining.Count), rng));
        }

        return selected;
    }

    private static List<Character> SelectExternalCharacters(
        IReadOnlyList<Organization> organizations,
        bool requiresKey,
        Random rng)
    {
        var selected = new List<Character>();
        var orgCharacterMap = organizations
            .Select(o => (Organization: o, Characters: GetOrganizationCharacters(o)))
            .ToList();

        if (orgCharacterMap.Count == 0)
            return selected;

        Character? forcedKey = null;
        Organization? forcedOrg = null;

        if (requiresKey)
        {
            var keyCandidates = orgCharacterMap
                .SelectMany(m => m.Characters.Where(c => c.IsKeyCharacter)
                    .Select(c => (Character: c, Organization: m.Organization)))
                .ToList();

            if (keyCandidates.Count > 0)
            {
                var pick = keyCandidates[rng.Next(keyCandidates.Count)];
                forcedKey = pick.Character;
                forcedOrg = pick.Organization;
            }
        }

        foreach (var (organization, characters) in orgCharacterMap)
        {
            if (characters.Count == 0)
                continue;

            var count = rng.Next(1, 4);
            count = Math.Clamp(count, 1, characters.Count);

            var orgSelected = new List<Character>();
            if (forcedKey != null && forcedOrg?.Id == organization.Id)
            {
                orgSelected.Add(forcedKey);
            }

            var remaining = characters.Where(c => orgSelected.All(s => s.Id != c.Id)).ToList();
            var needed = count - orgSelected.Count;
            if (needed > 0)
            {
                orgSelected.AddRange(PickRandomDistinct(remaining, Math.Min(needed, remaining.Count), rng));
            }

            selected.AddRange(orgSelected);
        }

        return selected
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .ToList();
    }

    private static List<Role> BuildRoleParticipants(
        IReadOnlyList<Character> characters,
        IReadOnlyList<Organization> organizations)
    {
        if (characters.Count == 0)
            return new List<Role>();

        var roleLookup = new Dictionary<Guid, Role>();
        foreach (var assignment in organizations.SelectMany(o => o.EnumerateCharacters()))
        {
            if (!roleLookup.ContainsKey(assignment.Character.Id))
            {
                roleLookup[assignment.Character.Id] = assignment.Role;
            }
        }

        return characters
            .Select(c => roleLookup.TryGetValue(c.Id, out var role) ? role : null)
            .Where(r => r != null)
            .GroupBy(r => r!.Id)
            .Select(g => g.First()!)
            .ToList();
    }

    private static List<Character> GetOrganizationCharacters(Organization organization)
    {
        return organization.EnumerateCharacters()
            .Select(a => a.Character)
            .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Email))
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .ToList();
    }

    private static bool OrganizationHasKeyCharacter(Organization organization)
    {
        return organization.EnumerateCharacters()
            .Any(a => a.Character.IsKeyCharacter && !string.IsNullOrWhiteSpace(a.Character.Email));
    }

    private static List<T> PickRandomDistinct<T>(List<T> items, int count, Random rng)
    {
        if (items.Count == 0 || count <= 0)
            return new List<T>();

        if (count >= items.Count)
            return new List<T>(items);

        var copy = new List<T>(items);
        for (var i = 0; i < count; i++)
        {
            var swapIndex = rng.Next(i, copy.Count);
            (copy[i], copy[swapIndex]) = (copy[swapIndex], copy[i]);
        }

        return copy.Take(count).ToList();
    }
}
