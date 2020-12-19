using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Hogwarts;
using UnityEngine;

using Rnd = UnityEngine.Random;

/// <summary>
/// On the Subject of Hogwarts
/// Created by Timwi
/// </summary>
public class HogwartsModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMSelectable ModuleSelectable;
    public KMAudio Audio;
    public KMBossModule BossModule;

    public KMSelectable LeftBtn;
    public KMSelectable RightBtn;
    public KMSelectable[] Stage2Houses;
    public MeshRenderer ModuleBackground;

    public Texture[] HouseBanners;
    public Texture[] HouseSeals;
    public Texture StandardBackground;
    public TextMesh ParchmentText;

    public GameObject Stage1;
    public GameObject Stage2;

    // This list does not need to contain Divided Squares because Divided Squares has Hogwarts on its “List M”, which means you can always solve Divided Squares even if Hogwarts is still waiting
    private static readonly string[] _defaultIgnoredModules = new[] { "Forget Everything", "Forget Me Not", "Hogwarts", "Souvenir", "The Swan", "Simon's Stages", "Forget This", "Alchemy", "Cookie Jars" };
    private string[] _ignoredModules;

    private static int _moduleIdCounter = 1;
    private int _moduleId;

    private List<Assoc> _moduleAssociations;
    private int _selectedIndex = 0;
    private readonly Dictionary<House, int> _points = new Dictionary<House, int>();
    private readonly Dictionary<House, string> _moduleNames = new Dictionary<House, string>();  // only used by Souvenir
    private bool _isStage2 = false;
    private bool _isSolved = false;
    private bool _strikeOnTie = true;

    void Start()
    {
        _moduleId = _moduleIdCounter++;
        LeftBtn.OnInteract = buttonPress(LeftBtn, left: true);
        RightBtn.OnInteract = buttonPress(RightBtn, left: false);
        for (int i = 0; i < 4; i++)
            Stage2Houses[i].OnInteract = stage2Select(Stage2Houses[i], (House) i);
        Stage2.SetActive(false);

        StartCoroutine(initialization());
    }

    IEnumerator initialization()
    {
        yield return null;
        _ignoredModules = BossModule.GetIgnoredModules(Module, _defaultIgnoredModules);
        Debug.LogFormat(@"<Hogwarts #{0}> Ignored modules: {1}", _moduleId, _ignoredModules.Join(", "));

        var retries = 0;
        retry:
        // Find out what distinct modules are on the bomb. (Filter out Hogwartses so that it cannot softlock on itself even if the ignore list gets messed up.)
        var modules = Bomb.GetSolvableModuleNames().Where(s => s != "Hogwarts").Distinct().Except(_ignoredModules).ToList().Shuffle();
        var founders = new[] { "GODRICGRYFFINDOR", "ROWENARAVENCLAW", "SALAZARSLYTHERIN", "HELGAHUFFLEPUFF" };
        var offset = Rnd.Range(0, 4);
        _moduleAssociations = modules.Select((m, ix) => new Assoc((House) ((ix + offset) % 4), m, founders[(ix + offset) % 4].GroupBy(ch => ch).Sum(gr => gr.Count() * m.Count(c => char.ToUpperInvariant(c) == gr.Key)))).ToList();

        for (var i = 0; i < 4; i++)
        {
            var h = (House) i;
            if (!_moduleAssociations.Any(asc => asc.House == h))
                _points[h] = -1;
        }

        if (_moduleAssociations.Count == 0)
        {
            Debug.LogFormat(@"[Hogwarts #{0}] No suitable modules on the bomb to solve.", _moduleId);
            _strikeOnTie = false;
            for (int h = 0; h < 4; h++)
                _points[(House) h] = 0;
            ActivateStage2();
            yield break;
        }
        else
            select(0);

        // Special case: if at least two houses have only one module, and that module gives them the same score, and this will cause a tie for the House Cup, then we can’t give a strike because the tie is unavoidable
        var info = Enumerable.Range(0, 4)
            .Select(i => !_moduleAssociations.Any(asc => asc.House == (House) i) ? null : new
            {
                MaxPoints = _moduleAssociations.Max(asc => asc.House == (House) i ? asc.Points : 0),
                NumModules = _moduleAssociations.Count(asc => asc.House == (House) i)
            })
            .ToArray();
        for (int h1 = 0; h1 < 4; h1++)
            for (int h2 = h1 + 1; h2 < 4; h2++)
                if (info[h1] != null && info[h2] != null && info[h1].NumModules == 1 && info[h2].NumModules == 1 && info[h1].MaxPoints == info[h2].MaxPoints
                    && Enumerable.Range(0, 4).All(h3 => h3 == h1 || h3 == h2 || info[h3] == null || info[h3].MaxPoints < info[h1].MaxPoints))
                {
                    retries++;
                    if (retries >= 100)
                    {
                        Debug.LogFormat(@"[Hogwarts #{0}] Not possible to avoid a tie for the House Cup. You will not receive a strike.", _moduleId);
                        _strikeOnTie = false;
                        goto noWorries;
                    }
                    goto retry;
                }

        noWorries:;
        for (int i = 0; i < _moduleAssociations.Count; i++)
            Debug.LogFormat(@"[Hogwarts #{0}] {1} = {2} ({3} points)", _moduleId, _moduleAssociations[i].Module, _moduleAssociations[i].House, _moduleAssociations[i].Points);
    }

    private KMSelectable.OnInteractHandler stage2Select(KMSelectable button, House house)
    {
        return delegate
        {
            if (_isSolved)
                return false;
            button.AddInteractionPunch();
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, button.transform);
            var maxScore = _points.Max(kvp => kvp.Value);
            if (_points[house] == maxScore)
            {
                Debug.LogFormat(@"[Hogwarts #{0}] Pressed {1}. Module solved.", _moduleId, house);
                Module.HandlePass();
                Audio.PlaySoundAtTransform(house + " wins", transform);
                _isSolved = true;
            }
            else
            {
                Debug.LogFormat(@"[Hogwarts #{0}] Pressed {1}. Strike.", _moduleId, house);
                Module.HandleStrike();
            }
            return false;
        };
    }

    private KMSelectable.OnInteractHandler buttonPress(KMSelectable button, bool left)
    {
        return delegate
        {
            button.AddInteractionPunch();
            Audio.PlaySoundAtTransform("Click" + Rnd.Range(1, 10), button.transform);
            select((_selectedIndex + (left ? -1 : 1) + _moduleAssociations.Count) % _moduleAssociations.Count);
            return false;
        };
    }

    private void select(int index)
    {
        _selectedIndex = index;
        var assoc = _moduleAssociations[_selectedIndex];
        ModuleBackground.material.mainTexture = HouseBanners[(int) assoc.House];

        if (assoc.Module.Length > 21)
        {
            var m = assoc.Module.Length / 2;
            var p1 = assoc.Module.LastIndexOfAny(new[] { ' ', '-' }, m);
            var p2 = assoc.Module.IndexOfAny(new[] { ' ', '-' }, m);
            var p = p1 == -1 ? p2 : p2 == -1 ? p1 : m - p1 < p2 - m ? p1 : p2;
            ParchmentText.text = p == -1 ? assoc.Module : assoc.Module.Substring(0, p) + (assoc.Module[p] == '-' ? "-" : "") + "\n" + assoc.Module.Substring(p + 1);
        }
        else
            ParchmentText.text = assoc.Module;
    }

    private string[] _prevSolvedModules = new string[0];

    private void Update()
    {
        if (_isSolved || _isStage2)
            return;

        var newSolvedModules = Bomb.GetSolvedModuleNames();
        if (newSolvedModules.Count == _prevSolvedModules.Length)
            return;

        var newSolvedModulesCopy = newSolvedModules.ToArray();

        // Remove all the modules that were already solved before, leaving only the newest solved module(s)
        foreach (var module in _prevSolvedModules)
            newSolvedModules.Remove(module);
        _prevSolvedModules = newSolvedModulesCopy;

        var selAssoc = _moduleAssociations[_selectedIndex];
        foreach (var solvedModule in newSolvedModules)
        {
            if (solvedModule == selAssoc.Module)
            {
                Debug.LogFormat(@"[Hogwarts #{0}] You solved {1} while it was selected, earning {2} points for {3}.", _moduleId, selAssoc.Module, selAssoc.Points, selAssoc.House);
                _points[selAssoc.House] = selAssoc.Points;
                _moduleNames[selAssoc.House] = selAssoc.Module;

                // Remove all the other modules for the same house
                _moduleAssociations.RemoveAll(asc => asc.House == selAssoc.House);
                Audio.PlaySoundAtTransform("Solve" + Rnd.Range(1, 6), transform);
                _selectedIndex = Rnd.Range(0, _moduleAssociations.Count);
            }
            else
            {
                Debug.LogFormat(@"[Hogwarts #{0}] You solved {1} while it was NOT selected.", _moduleId, solvedModule);
                var curSel = _moduleAssociations[_selectedIndex].Module;
                _moduleAssociations.RemoveAll(asc => asc.Module == solvedModule);
                _selectedIndex = _moduleAssociations.IndexOf(asc => asc.Module == curSel);

                // Is there a house that is now losing out entirely?
                var missedHouse = Enumerable.Range(0, 4).IndexOf(house => !_points.ContainsKey((House) house) && !_moduleAssociations.Any(asc => asc.House == (House) house && !_ignoredModules.Contains(asc.Module)));
                if (missedHouse != -1)
                {
                    Audio.PlaySoundAtTransform("Strike", transform);
                    Debug.LogFormat(@"[Hogwarts #{0}] Strike because you solved all {1} modules unselected.", _moduleId, (House) missedHouse);
                    Module.HandleStrike();
                    _points[(House) missedHouse] = -1;
                }
            }

            if (_moduleAssociations.Count == 0)
            {
                Audio.PlaySoundAtTransform("Transition" + Rnd.Range(1, 4), transform);
                ActivateStage2();
                break;
            }
            select(_selectedIndex);
        }
    }

    private void ActivateStage2()
    {
        Stage1.SetActive(false);
        Stage2.SetActive(true);
        _isStage2 = true;
        ModuleBackground.material.mainTexture = StandardBackground;
        var maxScore = _points.Max(kvp => kvp.Value);
        var houses = _points.Where(kvp => kvp.Value == maxScore).Select(kvp => kvp.Key).ToArray();
        if (houses.Length > 1 && _strikeOnTie)
        {
            Debug.LogFormat(@"[Hogwarts #{0}] Strike because there is a tie between {1}.", _moduleId, houses.JoinString(" and "));
            Module.HandleStrike();
        }
        Debug.LogFormat(@"[Hogwarts #{0}] Stage 2 activated. Correct answer{1}: {2}", _moduleId, houses.Length > 1 ? "s" : null, houses.JoinString(", "));
    }

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"!{0} find color [Scroll to the next module containing “color” in the name] | !{0} cycle 5 [Cycle forward 5 entries] | !{0} gryffindor/ravenclaw/slytherin/hufflepuff";
#pragma warning restore 414

    IEnumerator ProcessTwitchCommand(string command)
    {
        command = command.ToUpperInvariant().Trim();

        Match m;
        if ((m = Regex.Match(command, @"^\s*find\s+(.*?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            if (_isStage2)
            {
                yield return "sendtochaterror You’re already in Stage 2. Please select a House.";
                yield break;
            }

            var ixs = Enumerable.Range(0, _moduleAssociations.Count).Where(i => _moduleAssociations[i].Module.ContainsIgnoreCase(m.Groups[1].Value)).ToArray();
            if (ixs.Length == 0)
            {
                yield return string.Format("sendtochat No modules containing “{0}”.", m.Groups[1].Value);
                yield break;
            }
            if (ixs.Length == 1 && ixs[0] == _selectedIndex)
            {
                yield return string.Format("sendtochat No other modules containing “{0}”.", m.Groups[1].Value);
                yield break;
            }
            yield return null;

            var nextIxIx = ixs.IndexOf(i => i > _selectedIndex);
            var newIx = ixs[(nextIxIx == -1) ? 0 : nextIxIx];
            var forwardsDistance = newIx > _selectedIndex ? newIx - _selectedIndex : newIx + _moduleAssociations.Count - _selectedIndex;
            var backwardsDistance = newIx < _selectedIndex ? _selectedIndex - newIx : _selectedIndex + _moduleAssociations.Count - newIx;
            var buttonToPress = (forwardsDistance <= backwardsDistance) ? RightBtn : LeftBtn;

            while (_selectedIndex != newIx)
            {
                buttonToPress.OnInteract();
                yield return new WaitForSeconds(.25f);
                yield return "trycancel";
            }
            yield break;
        }

        int num;
        if ((m = Regex.Match(command, @"^\s*cycle\s+(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success && int.TryParse(m.Groups[1].Value, out num))
        {
            if (_isStage2)
            {
                yield return "sendtochaterror You’re already in Stage 2. Please select a House.";
                yield break;
            }

            yield return null;
            for (int i = 0; i < num; i++)
            {
                RightBtn.OnInteract();
                yield return new WaitForSeconds(1.25f);
                yield return "trycancel";
            }
            yield break;
        }

        if ((m = Regex.Match(command, @"^\s*(g(ryffindor)?|r(avenclaw)?|s(lytherin)?|h(ufflepuff)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            if (!_isStage2)
            {
                yield return "sendtochaterror You’re not in Stage 2 yet. Please solve some modules first.";
                yield break;
            }

            yield return null;
            Stage2Houses["grshGRSH".IndexOf(m.Groups[1].Value[0]) % 4].OnInteract();
            yield break;
        }
    }
}
