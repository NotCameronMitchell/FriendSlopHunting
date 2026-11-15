using UnityEngine;

namespace FriendSlop.Game
{
    /// <summary>
    /// The host's announcement when a hunter goes down. A pool of lines per kind of death (with
    /// extra ones for specific animals) so the same message rarely shows twice in a row.
    /// {v} = victim, {a} = attacker (animal name), {k} = killer (player), {w} = weapon.
    /// </summary>
    public static class DeathLines
    {
        static readonly string[] Animal =
        {
            "{v} got wrecked by a {a}!",
            "{v} was trampled by a {a}.",
            "A {a} sent {v} back to camp.",
            "{v} lost a fight with a {a}. Embarrassing.",
            "{v} became {a} chow.",
            "{a} 1, {v} 0.",
            "{v} tried to pet a {a}.",
            "The {a} has claimed {v}.",
            "{v} forgot that {a}s fight back.",
            "{v} got bodied by a {a}. Thoughts and prayers.",
            "{v} was outplayed by a {a}.",
            "Breaking news: local hunter {v} defeated by {a}.",
        };

        static readonly string[] Bird = { "{v} got honked into oblivion.", "{v} was pecked to death. By a {a}.", "{v} has been goosed. Fatally." };
        static readonly string[] BigHoofed = { "{v} got moosed.", "A {a} sat on {v}.", "{v} got antlered.", "{v} was flattened by half a ton of {a}." };
        static readonly string[] Bear = { "{v} got a bear hug. A lethal one.", "{v} was mauled. Classic bear behaviour.", "The {a} ate {v}'s lunch. And {v}." };
        static readonly string[] Pig = { "{v} got gored by a {a}.", "{v} was turned into bacon's revenge.", "The {a} trotted right over {v}." };
        static readonly string[] Canine = { "{v} was chased down by a {a}.", "{v} got nibbled to death by a {a}.", "The {a} pack wins this round, {v}." };
        static readonly string[] Boss = { "{a} flattened {v}!", "{v} challenged {a} and lost. Badly.", "{a} added {v} to the trophy wall.", "{v} got absolutely destroyed by {a}." };
        static readonly string[] Stink = { "{v} died of skunk stink. Nobody's coming to the funeral.", "{v} inhaled the skunk.", "{v} smelled something awful. Then nothing at all." };
        static readonly string[] Friendly =
        {
            "{k} shot {v} with the {w}. Friendship over.",
            "{v} was 'accidentally' shot by {k}.",
            "{k} mistook {v} for a deer.",
            "{k} bagged a {v}! Wait...",
            "{k} and {v} need to talk about that {w} shot.",
            "{v} took one for the team. From {k}.",
        };
        static readonly string[] Explosive = { "{k} turned {v} into confetti with the {w}.", "{v} was launched into orbit by {k}.", "{k}'s {w} sent {v} back to camp in pieces." };

        static string _last;

        /// <summary>Pick a death line. killer is a player name for friendly fire, else null.</summary>
        public static string Pick(string victim, string attacker, string killer = null, string weapon = null, bool boss = false)
        {
            string[] pool;
            string a = attacker ?? "something";
            if (!string.IsNullOrEmpty(killer))
                pool = weapon == "Bazooka" ? Explosive : Friendly;
            else if (boss) pool = Boss;
            else if (a.Contains("stink")) pool = Stink;
            else
            {
                string l = a.ToLowerInvariant();
                string[] extra = l.Contains("goose") || l.Contains("duck") || l.Contains("turkey") ? Bird
                    : l.Contains("moose") || l.Contains("elk") || l.Contains("deer") || l.Contains("stag") || l.Contains("goat") ? BigHoofed
                    : l.Contains("bear") ? Bear
                    : l.Contains("boar") || l.Contains("hog") ? Pig
                    : l.Contains("wolf") || l.Contains("coyote") || l.Contains("fox") ? Canine
                    : null;
                // Half the time use an animal-specific line when there is one.
                pool = extra != null && Random.value < 0.5f ? extra : Animal;
            }
            string line = pool[Random.Range(0, pool.Length)];
            for (int i = 0; i < 3 && line == _last && pool.Length > 1; i++) line = pool[Random.Range(0, pool.Length)];
            _last = line;
            return line.Replace("{v}", victim).Replace("{a}", a).Replace("{k}", killer ?? "").Replace("{w}", weapon ?? "");
        }
    }
}
