using System.Text.Json;
using Rusty.Engine;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Campaigns;

/// <summary>
/// The campaign command surface: start a campaign, then take commands
/// (move, turn, choose, look, status). Each call returns what happened as
/// facts. The CLI and the Game drive the same commands, passing Engine
/// Random from inside their host callback; the runner rolls command n on
/// scope goldbox.play.n from the campaign's seed, which is what lets a saved
/// game resume exactly.
/// </summary>
/// <exception cref="RuleFailure">A rule expression failed; it names the definition, file and path.</exception>
public sealed partial class CampaignRunner
{
    /// <summary>The commands play understands, for help text and errors.</summary>
    public const string CommandList = "forward, back, left, right, around, choose <n>, buy <n>, sell <n>, serve <service> <member>, train <member> [level choices], leave, look, status, level <member> [--class <id>] [--feature <id>,...] [--boosts <id>,...], former <member> on|off";

    private const int MaxChainLength = 10_000;

    private readonly RuleSet _rules;
    private readonly CampaignState _state;
    private readonly Dictionary<Definition, AreaMap> _maps = [];

    public CampaignRunner(RuleSet rules, CampaignState state)
    {
        _rules = rules;
        _state = state;
    }

    public CampaignState State => _state;

    /// <summary>A new campaign: the party at the start entry, variables at their initial values.</summary>
    public static CampaignState NewState(RuleSet rules, Definition campaign, IEnumerable<Character> party, ulong seed)
    {
        Definition area = rules.Reference(campaign, "$.start.area");
        CampaignState state = new() { Campaign = campaign, Seed = seed, Area = area };
        state.Party.AddRange(party);
        foreach (Character character in state.Party)
        {
            // A new adventure: only a character already calling on a former class forfeits its experience.
            character.ForfeitsExperience = character.UsesFormerClasses;
        }

        Evaluator evaluator = new(rules, null);
        foreach (Definition variable in rules.Variables.Values)
        {
            state.Variables[variable.Id] = Located(variable, "$.initial", () => evaluator.Evaluate(rules.Expression(variable, "$.initial"), null, null));
        }

        JsonElement entry = area.Json.GetProperty("entries").GetProperty(campaign.Json.GetProperty("start").GetProperty("entry").GetString()!);
        PlaceAt(state, area, entry);
        return state;
    }

    /// <summary>Begins play: where the party is, then the campaign's intro event. Rolls on scope goldbox.play.0.</summary>
    public List<PlayFact> Begin(IRandomService random)
    {
        return WithDice(random, Begin);
    }

    /// <summary>Takes the next command, rolling on its own scope.</summary>
    public List<PlayFact> Execute(string command, IRandomService random)
    {
        _state.Commands++;
        return WithDice(random, dice => Execute(command, dice));
    }

    private List<PlayFact> WithDice(IRandomService random, Func<DiceRoller, List<PlayFact>> step)
    {
        using Rng stream = random.CreateScoped(new ScopedRngCreateRequest(_state.Seed, $"goldbox.play.{_state.Commands}"));
        return step(new DiceRoller(random, stream));
    }

    private List<PlayFact> Begin(DiceRoller dice)
    {
        List<PlayFact> facts = [new ArrivedFact(_state.Area.Name, _state.X, _state.Y, _state.Facing)];
        if (_state.Campaign.Json.TryGetProperty("intro", out _))
        {
            RunChain(_rules.Reference(_state.Campaign, "$.intro"), dice, facts);
        }

        return facts;
    }

    /// <summary>One command. Unknown or impossible commands give a refused fact and change nothing.</summary>
    private List<PlayFact> Execute(string command, DiceRoller dice)
    {
        List<PlayFact> facts = [];
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (_state.Ended)
        {
            facts.Add(new RefusedFact("the adventure has ended."));
            return facts;
        }

        string verb = words.Length == 0 ? "" : words[0].ToLowerInvariant();
        if (_state.PendingMenu is not null && verb is not ("choose" or "look" or "status"))
        {
            facts.Add(new RefusedFact($"choose an option first (choose <n>)."));
            return facts;
        }

        if (_state.PendingShop is not null && verb is not ("buy" or "sell" or "leave" or "look" or "status"))
        {
            facts.Add(new RefusedFact("leave the shop first, or trade with buy <n> or sell <n>."));
            return facts;
        }

        if (_state.PendingTemple is not null && verb is not ("serve" or "leave" or "look" or "status"))
        {
            facts.Add(new RefusedFact("leave the temple first, or use serve <service> <member>."));
            return facts;
        }

        if (_state.PendingTraining is not null && verb is not ("train" or "leave" or "look" or "status"))
        {
            facts.Add(new RefusedFact("leave the trainer first, or use train <member> with level choices."));
            return facts;
        }

        switch (verb)
        {
            case "forward" or "back" when words.Length == 1:
                Move(verb == "forward" ? _state.Facing : Facings.Turn(_state.Facing, 2), dice, facts);
                break;
            case "left" or "right" or "around" when words.Length == 1:
                _state.Facing = Facings.Turn(_state.Facing, verb == "left" ? -1 : verb == "right" ? 1 : 2);
                _state.Picture = null;
                facts.Add(new TurnedFact(_state.Facing));
                break;
            case "choose" when words.Length == 2 && int.TryParse(words[1], out int number):
                Choose(number, dice, facts);
                break;
            case "buy" when words.Length == 2 && int.TryParse(words[1], out int stock):
                Buy(stock, facts);
                break;
            case "sell" when words.Length == 2 && int.TryParse(words[1], out int carried):
                Sell(carried, facts);
                break;
            case "leave" when words.Length == 1:
                if (_state.PendingTemple is not null)
                {
                    LeaveTemple(dice, facts);
                }
                else if (_state.PendingTraining is Definition trainer)
                {
                    _state.PendingTraining = null;
                    facts.Add(new TextFact("The party leaves the trainer."));
                    if (Next(trainer, "$.next") is Definition next)
                    {
                        RunChain(next, dice, facts);
                    }
                }
                else
                {
                    LeaveShop(dice, facts);
                }
                break;
            case "serve" when words.Length == 3 && int.TryParse(words[1], out int service) && int.TryParse(words[2], out int patient):
                Serve(service, patient, dice, facts);
                break;
            case "look" when words.Length == 1:
                facts.Add(Look());
                break;
            case "status" when words.Length == 1:
                facts.Add(Status());
                if (Shop() is ShopFact shop)
                {
                    facts.Add(shop);
                }

                if (Temple() is TempleFact temple)
                {
                    facts.Add(temple);
                }

                if (Training() is TextFact training)
                {
                    facts.Add(training);
                }

                break;
            case "train" when words.Length >= 2 && int.TryParse(words[1], out int trainee):
                Train(trainee, words[2..], dice, facts);
                break;
            case "level" when words.Length >= 2 && int.TryParse(words[1], out int member):
                Level(member, words[2..], dice, facts);
                break;
            case "former" when words.Length == 3 && int.TryParse(words[1], out int caller) && words[2] is "on" or "off":
                Former(caller, words[2] == "on", facts);
                break;
            default:
                facts.Add(new RefusedFact($"'{command}' is not a command. Commands: {CommandList}."));
                break;
        }

        return facts;
    }

    private void Move(Facing direction, DiceRoller dice, List<PlayFact> facts)
    {
        AreaMap map = Map(_state.Area);
        Edge edge = map.EdgeOf(_state.X, _state.Y, direction);
        (int dx, int dy) = Facings.Step(direction);
        (int x, int y) = (_state.X + dx, _state.Y + dy);
        if (edge == Edge.Wall)
        {
            facts.Add(new RefusedFact($"a wall blocks the way {Facings.Name(direction)}."));
            return;
        }

        if (!map.Contains(x, y))
        {
            facts.Add(new RefusedFact($"the edge of {_state.Area.Name} is {Facings.Name(direction)}."));
            return;
        }

        (_state.X, _state.Y) = (x, y);
        _state.Picture = null;
        facts.Add(new MovedFact(x, y, _state.Facing));
        Trigger(dice, facts);
    }

    /// <summary>Runs the event of the cell the party just entered, if its facing and once-only rules allow.</summary>
    private void Trigger(DiceRoller dice, List<PlayFact> facts)
    {
        if (CellOf(_state.Area, _state.X, _state.Y) is not (JsonElement cell, int index) || !cell.TryGetProperty("event", out _))
        {
            return;
        }

        if (cell.TryGetProperty("facing", out JsonElement facing) && facing.GetString() != Facings.Name(_state.Facing))
        {
            return;
        }

        if (cell.TryGetProperty("once", out JsonElement once) && once.GetBoolean())
        {
            string key = $"{_state.Area.QualifiedId}@{_state.X},{_state.Y}";
            if (!_state.Fired.Add(key))
            {
                return;
            }
        }

        RunChain(_rules.Reference(_state.Area, $"$.cells[{index}].event"), dice, facts);
    }

    private void Choose(int number, DiceRoller dice, List<PlayFact> facts)
    {
        if (_state.PendingMenu is not Definition menu)
        {
            facts.Add(new RefusedFact("there is nothing to choose."));
            return;
        }

        List<(int Number, string Label, int Index)> offered = Offered(menu);
        (int Number, string Label, int Index)? picked = offered.FirstOrDefault(option => option.Number == number);
        if (picked is not (int, string label, int index) || picked.Value.Number == 0)
        {
            facts.Add(new RefusedFact($"{number} is not one of the options: {string.Join(", ", offered.Select(option => option.Number))}."));
            return;
        }

        _state.PendingMenu = null;
        facts.Add(new ChoseFact(number, label));
        JsonElement option = menu.Json.GetProperty("options")[index];
        if (option.TryGetProperty("next", out _))
        {
            RunChain(_rules.Reference(menu, $"$.options[{index}].next"), dice, facts);
        }
    }

    /// <summary>Runs events from <paramref name="start"/> until one waits for a choice, the chain ends, or the adventure does.</summary>
    private void RunChain(Definition start, DiceRoller dice, List<PlayFact> facts)
    {
        Definition? current = start;
        int steps = 0;
        while (current is not null && !_state.Ended)
        {
            if (++steps > MaxChainLength)
            {
                throw new RuleFailure(new ModuleDiagnostic("event.loop", $"The event chain ran {MaxChainLength} events without stopping; events loop back on each other without a menu or an end.", current.Module, current.File, "$"));
            }

            current = Run(current, dice, facts);
        }
    }

    /// <summary>Shows the event's picture, plays its sound and starts its music, as it begins.</summary>
    private void Present(Definition evt, List<PlayFact> facts)
    {
        Definition? picture = evt.Json.TryGetProperty("picture", out _) ? _rules.Reference(evt, "$.picture") : null;
        Definition? sound = evt.Json.TryGetProperty("sound", out _) ? _rules.Reference(evt, "$.sound") : null;
        Definition? music = evt.Json.TryGetProperty("music", out _) ? _rules.Reference(evt, "$.music") : null;
        if (picture is null && sound is null && music is null)
        {
            return;
        }

        _state.Picture = picture ?? _state.Picture;
        _state.Music = music ?? _state.Music;
        facts.Add(new MediaFact(picture, sound, music));
    }

    /// <summary>Runs one event and returns the next, or null when the chain stops here.</summary>
    private Definition? Run(Definition evt, DiceRoller dice, List<PlayFact> facts)
    {
        JsonElement json = evt.Json;
        Present(evt, facts);
        switch (json.GetProperty("kind").GetString())
        {
            case "text":
                facts.Add(new TextFact(json.GetProperty("text").GetString()!));
                return Next(evt, "$.next");
            case "menu":
                List<(int Number, string Label, int Index)> offered = Offered(evt);
                facts.Add(new MenuFact(json.GetProperty("text").GetString()!, offered.Select(option => (option.Number, option.Label)).ToList()));
                _state.PendingMenu = evt;
                return null;
            case "shop":
                _state.PendingShop = evt;
                facts.Add(Shop()!);
                return null;
            case "temple":
                _state.PendingTemple = evt;
                facts.Add(Temple()!);
                return null;
            case "training":
                _state.PendingTraining = evt;
                facts.Add(Training()!);
                return null;
            case "join" or "dismiss":
                return ChangeParty(evt, facts);
            case "set":
                Definition variable = _rules.Reference(evt, "$.variable");
                Value value = Evaluate(evt, "$.value", dice);
                _state.Variables[variable.Id] = value;
                facts.Add(new VariableFact(variable.Id, value));
                return Next(evt, "$.next");
            case "branch":
                int index = 0;
                foreach (JsonElement _ in json.GetProperty("branches").EnumerateArray())
                {
                    if (Evaluate(evt, $"$.branches[{index}].when", dice).Boolean)
                    {
                        return _rules.Reference(evt, $"$.branches[{index}].next");
                    }

                    index++;
                }

                return Next(evt, "$.otherwise");
            case "teleport":
                Definition area = _rules.Reference(evt, "$.area");
                PlaceAt(_state, area, area.Json.GetProperty("entries").GetProperty(json.GetProperty("entry").GetString()!));
                facts.Add(new ArrivedFact(area.Name, _state.X, _state.Y, _state.Facing));
                return Next(evt, "$.next");
            case "treasure":
                return Treasure(evt, dice, facts);
            case "give" or "take":
                return ChangeItems(evt, facts);
            case "experience":
                if (json.TryGetProperty("text", out JsonElement said))
                {
                    facts.Add(new TextFact(said.GetString()!));
                }

                decimal amount = Located(evt, "$.amount", () => Evaluate(evt, "$.amount", dice).Number);
                bool each = json.TryGetProperty("each", out JsonElement everyone) && everyone.GetBoolean();
                Award(evt, "$.amount", amount, each, _state.Party.Select(_ => true).ToList(), dice, facts);
                return Next(evt, "$.next");
            case "rest":
                return Rest(evt, dice, facts);
            case "combat":
                return Fight(evt, dice, facts);
            default:
                _state.Ended = true;
                facts.Add(new EndedFact(json.GetProperty("text").GetString()!));
                return null;
        }
    }

    private Definition? Treasure(Definition evt, DiceRoller dice, List<PlayFact> facts)
    {
        int before = dice.Rolls.Count;
        decimal gold = evt.Json.TryGetProperty("gold", out _) ? Evaluate(evt, "$.gold", dice).Number : 0;
        List<Definition> items = [];
        if (evt.Json.TryGetProperty("items", out JsonElement found))
        {
            for (int i = 0; i < found.GetArrayLength(); i++)
            {
                items.Add(_rules.Reference(evt, $"$.items[{i}]"));
            }
        }

        // One owner for money: the characters. Shares split evenly; the remainder goes to the first.
        if (gold != 0 && _state.Party.Count > 0)
        {
            decimal share = decimal.Floor(gold / _state.Party.Count);
            Located(evt, "$.gold", () =>
            {
                for (int i = 0; i < _state.Party.Count; i++)
                {
                    _state.Party[i].Gold = checked(_state.Party[i].Gold + share + (i == 0 ? gold - share * _state.Party.Count : 0));
                }

                return true;
            });
        }

        _state.Inventory.AddRange(items);
        facts.Add(new TreasureFact(gold, items.Select(item => item.Name).ToList()) { Rolls = dice.Rolls.Skip(before).ToList() });
        return Next(evt, "$.next");
    }

    /// <summary>
    /// Gives experience to the party: the whole amount to each (an event's
    /// each, or experience_to "each"), or an even share (rounding down) to
    /// those the ruleset's experience_to names
    /// (<paramref name="standing"/> for survivors). Levels that need no
    /// choice come at once; the others wait for a level command.
    /// </summary>
    private void Award(Definition owner, string path, decimal amount, bool each, List<bool> standing, DiceRoller dice, List<PlayFact> facts)
    {
        string? to = _rules.Advancement?.Json.TryGetProperty("experience_to", out JsonElement split) == true ? split.GetString() : null;
        each = each || to == "each";
        bool toParty = to == "party";
        List<int> sharing = Enumerable.Range(0, _state.Party.Count).Where(index => each || toParty || standing[index]).ToList();
        if (sharing.Count == 0)
        {
            facts.Add(new ExperienceFact([]));
            return;
        }

        decimal share = each ? amount : decimal.Floor(amount / sharing.Count);
        facts.Add(new ExperienceFact(sharing.Select(index => (_state.Party[index].Name, share, _state.Party[index].ForfeitsExperience)).ToList()));
        foreach (int index in sharing)
        {
            Character character = _state.Party[index];
            int before = dice.Rolls.Count;
            List<LevelGain> gains = Located(owner, path, () => CharacterRules.Award(_rules, character, share, dice));
            ReportLevels(character, index + 1, gains, before, dice, facts);
        }
    }

    /// <summary>The level command: takes the levels a character has the experience for, with the choices they need.</summary>
    private bool Level(int member, string[] options, DiceRoller dice, List<PlayFact> facts, bool trained = false)
    {
        if (member < 1 || member > _state.Party.Count)
        {
            facts.Add(new RefusedFact($"{member} is not a party member; members are 1 to {_state.Party.Count}."));
            return false;
        }

        if (CharacterRules.RequiresTraining(_rules) && !trained)
        {
            facts.Add(new RefusedFact("This ruleset requires training; use train <member> at a training event."));
            return false;
        }

        Dictionary<string, string> given = [];
        for (int index = 0; index < options.Length; index++)
        {
            if (options[index] is not ("--class" or "--feature" or "--boosts") || index + 1 >= options.Length || given.ContainsKey(options[index]))
            {
                facts.Add(new RefusedFact($"level takes a member number, then --class <id>, --feature <id>,... and --boosts <id>,..., each once."));
                return false;
            }

            given[options[index]] = options[++index];
        }

        Character character = _state.Party[member - 1];
        string? nextClass = given.GetValueOrDefault("--class");
        if (nextClass is null && !CharacterRules.ReadyToLevel(_rules, character))
        {
            facts.Add(new RefusedFact($"{character.Name} doesn't have the experience for another level."));
            return false;
        }

        static List<string>? List(string? text) => text?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        List<ModuleDiagnostic> problems = [];
        int before = dice.Rolls.Count;
        if (CharacterRules.AddExperience(_rules, character, 0, dice, problems, nextClass, List(given.GetValueOrDefault("--feature")), List(given.GetValueOrDefault("--boosts")), trained: trained, oneLevel: trained) is not List<LevelGain> gains)
        {
            facts.Add(new RefusedFact(string.Join(" ", problems.Select(problem => problem.Message))));
            return false;
        }

        if (gains.Count == 0)
        {
            facts.Add(new RefusedFact($"{character.Name} has no level available with those choices."));
            return false;
        }

        ReportLevels(character, member, gains, before, dice, facts);
        return true;
    }

    /// <summary>The former command: a dual-classed character calls on its dormant classes (forfeiting the adventure's experience) or stops.</summary>
    private void Former(int member, bool use, List<PlayFact> facts)
    {
        if (member < 1 || member > _state.Party.Count)
        {
            facts.Add(new RefusedFact($"{member} is not a party member; members are 1 to {_state.Party.Count}."));
            return;
        }

        Character character = _state.Party[member - 1];
        List<ModuleDiagnostic> problems = [];
        if (!CharacterRules.UseFormerClasses(_rules, character, use, problems))
        {
            facts.Add(new RefusedFact(problems[0].Message));
            return;
        }

        facts.Add(new TextFact(use
            ? $"{character.Name} calls on the old ways, and will earn no experience for the rest of this adventure."
            : $"{character.Name} sets the old ways aside."));
    }

    /// <summary>A fact for each level gained, then one if another level still waits for choices.</summary>
    private void ReportLevels(Character character, int member, List<LevelGain> gains, int rollsBefore, DiceRoller dice, List<PlayFact> facts)
    {
        string track = _rules.LevelTrack?.Name.ToLowerInvariant() ?? "level track";
        for (int index = 0; index < gains.Count; index++)
        {
            LevelGain gain = gains[index];
            LevelFact fact = new(character.Name, member, gain.Level, $"{gain.Class.Name} {character.ClassLevels().GetValueOrDefault(gain.Class)}", gain.Amount, track);
            facts.Add(index == 0 ? fact with { Rolls = dice.Rolls.Skip(rollsBefore).ToList() } : fact);
        }

        if (CharacterRules.ReadyToLevel(_rules, character))
        {
            facts.Add(CharacterRules.RequiresTraining(_rules)
                ? new TextFact($"{character.Name} has the experience for a new level; use train {member} at a training event.")
                : new LevelFact(character.Name, member, character.Level + 1, "", 0, track, Waiting: true));
        }
    }

    /// <summary>Who is in a fight and where they start on its track, for presenting it.</summary>
    private List<FightMember> Members(Definition combat, Definition encounter, List<CombatSide> sides)
    {
        Definition track = _rules.Reference(combat, "$.track");
        Evaluator evaluator = new(_rules, null);
        CombatField? field = CombatField.Of(combat, encounter);
        List<FightMember> members = [];
        for (int side = 0; side < sides.Count; side++)
        {
            // The same starting cells the combat runner deploys to.
            IReadOnlyList<Cell>? cells = field?.Deploy(side, sides[side].Members.Count);
            for (int index = 0; index < sides[side].Members.Count; index++)
            {
                Combatant member = sides[side].Members[index];
                Creature creature = member.Creature;
                decimal start = evaluator.TrackCurrent(creature, track);
                decimal? max = evaluator.KnownTrackMax(creature, track);
                members.Add(new FightMember(member.Name, side, creature.Monster, creature.Monster is null ? creature.Class : null, start, max, cells?[index]));
            }
        }

        return members;
    }

    private Definition? Fight(Definition evt, DiceRoller dice, List<PlayFact> facts)
    {
        Definition encounter = _rules.Reference(evt, "$.encounter");
        Definition combat = evt.Json.TryGetProperty("combat", out _)
            ? _rules.Reference(evt, "$.combat")
            : _rules.OfType(DefinitionTypes.Combat).Single();
        List<Combatant> party = _state.Party.Select(character => Combatant.FromCharacter(_rules, character)).ToList();
        List<CombatSide> sides = Encounters.Distinct(
        [
            new CombatSide("Party", party),
            new CombatSide(encounter.Name, Encounters.Spawn(_rules, encounter, dice)),
        ]);
        List<FightMember> members = Members(combat, encounter, sides);
        CombatResult result = CombatRunner.Run(_rules, combat, sides, dice, CombatRunner.RoundLimit(combat), encounter);

        // The party keeps what the fight did to its tracks.
        for (int i = 0; i < _state.Party.Count; i++)
        {
            foreach ((string id, TrackValue value) in sides[0].Members[i].Creature.Tracks)
            {
                _state.Party[i].Tracks[id] = new TrackValue { Current = value.Current, Max = _state.Party[i].Tracks.TryGetValue(id, out TrackValue? own) ? own.Max : null };
            }

            // And the prepared spells it cast.
            if (sides[0].Members[i].Preparing.Count > 0)
            {
                _state.Party[i].Prepared = sides[0].Members[i].Prepared.ToList();
            }
        }

        FightOutcome outcome = result.Winner switch
        {
            0 => FightOutcome.Won,
            null => FightOutcome.Undecided,
            _ => FightOutcome.Lost,
        };
        facts.Add(new FightFact(encounter.Name, result.Track, members, result.Facts, outcome, CombatField.Of(combat, encounter)));

        // Every monster felled is worth its experience, whoever won.
        decimal earned = sides[1].Members.Where(member => member.Defeated && !member.Escaped && member.Creature.Monster is not null)
            .Sum(member => member.Creature.Monster!.Json.GetProperty("xp").GetDecimal());
        if (earned > 0)
        {
            Award(evt, "$.encounter", earned, false, sides[0].Members.Select(member => !member.Defeated).ToList(), dice, facts);
        }
        if (outcome == FightOutcome.Won)
        {
            return Next(evt, "$.on_win");
        }

        if (outcome == FightOutcome.Undecided)
        {
            return Next(evt, "$.on_draw");
        }

        if (evt.Json.TryGetProperty("on_lose", out _))
        {
            return Next(evt, "$.on_lose");
        }

        _state.Ended = true;
        facts.Add(new EndedFact("The party has fallen."));
        return null;
    }

    /// <summary>
    /// Evaluates a checked boolean expression of <paramref name="owner"/> (a
    /// cell's prop condition, say) against the campaign variables. It rolls
    /// nothing, so presentation can ask it at any time.
    /// </summary>
    public bool IsTrue(Definition owner, string path)
    {
        Evaluator evaluator = new(_rules, null);
        return Located(owner, path, () => evaluator.Evaluate(_rules.Expression(owner, path), new Scope(null, null, Variables: _state.Variables, PartyItems: _state.CarriedItems))).Boolean;
    }

    /// <summary>The waiting menu's options, numbered as <c>choose</c> takes them; empty when no menu waits.</summary>
    public IReadOnlyList<(int Number, string Label)> MenuOptions()
    {
        return _state.PendingMenu is Definition menu
            ? Offered(menu).Select(option => (option.Number, option.Label)).ToList()
            : [];
    }

    private List<(int Number, string Label, int Index)> Offered(Definition menu)
    {
        List<(int, string, int)> offered = [];
        int index = 0;
        foreach (JsonElement option in menu.Json.GetProperty("options").EnumerateArray())
        {
            if (!option.TryGetProperty("when", out _) || Evaluate(menu, $"$.options[{index}].when", null).Boolean)
            {
                offered.Add((offered.Count + 1, option.GetProperty("label").GetString()!, index));
            }

            index++;
        }

        return offered;
    }

    private LookFact Look()
    {
        AreaMap map = Map(_state.Area);
        List<string> sides = [];
        foreach (Facing side in new[] { _state.Facing, Facings.Turn(_state.Facing, 1), Facings.Turn(_state.Facing, 2), Facings.Turn(_state.Facing, 3) })
        {
            string what = map.EdgeOf(_state.X, _state.Y, side) switch
            {
                Edge.Wall or Edge.Secret => "wall",
                Edge.Door => "door",
                _ => "open",
            };
            sides.Add($"{Facings.Name(side)}: {what}");
        }

        string? zone = CellOf(_state.Area, _state.X, _state.Y) is (JsonElement cell, _) && cell.TryGetProperty("zone", out JsonElement found) ? found.GetString() : null;
        return new LookFact(_state.Area.Name, _state.X, _state.Y, _state.Facing, sides, zone);
    }

    private StatusFact Status()
    {
        Evaluator evaluator = new(_rules, null);
        List<string> lines = [];
        foreach (Character character in _state.Party)
        {
            Creature creature = character.ToCreature();
            string tracks = string.Join(", ", _rules.Tracks.Values
                .Select(track => (Track: track, Max: evaluator.TrackMax(creature, track)))
                .Where(entry => entry.Max != 0)
                .Select(entry => $"{entry.Track.Name.ToLowerInvariant()} {Fact(creature.Track(entry.Track.Id).Current ?? 0)}/{Fact(entry.Max)}"));
            string ready = (CharacterRules.ReadyToLevel(_rules, character) ? ", level ready" : "")
                + (character.UsesFormerClasses ? ", calling on former classes" : character.ForfeitsExperience ? ", forfeiting experience" : "");
            lines.Add($"{character.Name} ({tracks}, {Fact(character.Experience)} xp{ready})");
        }

        lines.Add($"gold {string.Join(" + ", _state.Party.Select(character => Fact(character.Gold)))}");
        if (_state.Inventory.Count > 0)
        {
            lines.Add($"carrying {string.Join(", ", _state.Inventory.Select(item => item.Name))}");
        }

        return new StatusFact(lines);
    }

    private static string Fact(decimal value) => value.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture);

    private Value Evaluate(Definition owner, string path, DiceRoller? dice)
    {
        Evaluator evaluator = new(_rules, dice);
        return Located(owner, path, () => evaluator.Evaluate(_rules.Expression(owner, path), new Scope(null, null, Variables: _state.Variables, PartyItems: _state.CarriedItems)));
    }

    private Definition? Next(Definition owner, string path)
    {
        return owner.Json.TryGetProperty(path[2..], out _) ? _rules.Reference(owner, path) : null;
    }

    private (JsonElement Cell, int Index)? CellOf(Definition area, int x, int y)
    {
        if (!area.Json.TryGetProperty("cells", out JsonElement cells))
        {
            return null;
        }

        int index = 0;
        foreach (JsonElement cell in cells.EnumerateArray())
        {
            JsonElement at = cell.GetProperty("at");
            if (at[0].GetInt32() == x && at[1].GetInt32() == y)
            {
                return (cell, index);
            }

            index++;
        }

        return null;
    }

    private AreaMap Map(Definition area)
    {
        if (!_maps.TryGetValue(area, out AreaMap? map))
        {
            map = AreaMap.Parse(area.Json.GetProperty("map").EnumerateArray().Select(row => row.GetString()!).ToList(), [])!;
            _maps[area] = map;
        }

        return map;
    }

    private static void PlaceAt(CampaignState state, Definition area, JsonElement entry)
    {
        state.Area = area;
        state.X = entry.GetProperty("at")[0].GetInt32();
        state.Y = entry.GetProperty("at")[1].GetInt32();
        Facings.TryParse(entry.GetProperty("facing").GetString()!, out Facing facing);
        state.Facing = facing;
    }

    private static T Located<T>(Definition owner, string path, Func<T> work)
    {
        try
        {
            return work();
        }
        catch (Exception exception) when (exception is ExpressionException or OverflowException)
        {
            string message = exception is OverflowException ? "A result is too large to be a number." : exception.Message;
            throw new RuleFailure(new ModuleDiagnostic("event.evaluate", message, owner.Module, owner.File, path));
        }
    }
}
