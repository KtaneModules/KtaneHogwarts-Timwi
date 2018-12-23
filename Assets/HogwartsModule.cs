using System.Collections.Generic;
using System.Linq;
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
    public KMAudio Audio;
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

    private static readonly string[] _specialModules = new[] { "Hogwarts", "Forget Everything", "Forget Me Not", "Souvenir", "The Swan" };
    private static int _moduleIdCounter = 1;
    private int _moduleId;

    private List<Assoc> _moduleAssociations;
    private int _selectedIndex = 0;
    private readonly Dictionary<House, int> _points = new Dictionary<House, int>();
    private bool _isStage2 = false;
    private bool _isSolved = false;
    private bool _tieAllowed = false;

    void Start()
    {
        _moduleId = _moduleIdCounter++;
        LeftBtn.OnInteract = buttonPress(LeftBtn, left: true);
        RightBtn.OnInteract = buttonPress(RightBtn, left: false);
        for (int i = 0; i < 4; i++)
            Stage2Houses[i].OnInteract = stage2Select(Stage2Houses[i], (House) i);
        Stage2.SetActive(false);

        // Find out what distinct modules are on the bomb.
        var allModules = Bomb.GetSolvableModuleNames();
        // Remove only ONE copy of Hogwarts
        allModules.Remove("Hogwarts");

        var retries = 0;
        retry:
        // Place the “special” modules last in the list so that every house is equally likely to get a non-special module. This relies on .OrderBy() being a stable sort
        var modules = allModules.Distinct().ToList().Shuffle().OrderBy(m => _specialModules.Contains(m)).ToList();
        var founders = new[] { "GODRICGRYFFINDOR", "ROWENARAVENCLAW", "SALAZARSLYTHERIN", "HELGAHUFFLEPUFF" };
        var offset = Rnd.Range(0, 4);
        _moduleAssociations = modules.Select((m, ix) => new Assoc((House) ((ix + offset) % 4), m, founders[(ix + offset) % 4].GroupBy(ch => ch).Sum(gr => gr.Count() * m.Count(c => char.ToUpperInvariant(c) == gr.Key)))).ToList();

        for (var i = 0; i < 4; i++)
        {
            var h = (House) i;
            if (_moduleAssociations.All(asc => asc.House != h || _specialModules.Contains(asc.Module) || asc.Module == "Hogwarts"))
            {
                _moduleAssociations.RemoveAll(asc => asc.House == h);
                _points[h] = -1;
            }
        }

        if (_moduleAssociations.Count == 0)
        {
            Debug.LogFormat(@"[Hogwarts #{0}] No suitable modules on the bomb to solve.", _moduleId);
            for (int i = 0; i < 4; i++)
                _points[(House) i] = 0;
            ActivateStage2();
            return;
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
                if (info[h1] != null && info[h2] != null && info[h1].NumModules == 1 && info[h2].NumModules == 1 && info[h1].MaxPoints == info[h2].MaxPoints && Enumerable.Range(0, 4).All(h3 => h3 == h1 || h3 == h2 || info[h3] == null || info[h3].MaxPoints < info[h1].MaxPoints))
                {
                    retries++;
                    if (retries >= 100)
                    {
                        Debug.LogFormat(@"[Hogwarts #{0}] Not possible to avoid a tie for the House Cup.", _moduleId);
                        for (int h = 0; h < 4; h++)
                            _points[(House) h] = 0;
                        ActivateStage2();
                        return;
                    }
                    for (int i = 0; i < _moduleAssociations.Count; i++)
                        Debug.LogFormat(@"<Hogwarts #{0}> {1} = {2} ({3} points)", _moduleId, _moduleAssociations[i].Module, _moduleAssociations[i].House, _moduleAssociations[i].Points);
                    Debug.LogFormat(@"<Hogwarts #{0}> UNAVOIDABLE TIE! Retrying...", _moduleId);
                    goto retry;
                }

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
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, button.transform);
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
        if (_isStage2)
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

                // Is there a tie that can no longer be broken?
                var maxScore = _points.Max(kvp => kvp.Value);
                if (!_tieAllowed && _points.Count(kvp => kvp.Value == maxScore) > 1 && !_moduleAssociations.Any(asc => asc.Points > maxScore))
                {
                    Debug.LogFormat(@"[Hogwarts #{0}] Strike because there is now a tie between {1} that can no longer be broken by another house.", _moduleId, _points.Where(kvp => kvp.Value == maxScore).Select(kvp => kvp.Key).JoinString(" and "));
                    Module.HandleStrike();
                }

                // Remove all the other modules for the same house
                _moduleAssociations.RemoveAll(asc => asc.House == selAssoc.House);
                if (_moduleAssociations.Count == 0)
                {
                    ActivateStage2();
                    break;
                }
                select(Rnd.Range(0, _moduleAssociations.Count));
            }
            else
            {
                Debug.LogFormat(@"[Hogwarts #{0}] You solved {1} while it was NOT selected.", _moduleId, solvedModule);
                var curSel = _moduleAssociations[_selectedIndex].Module;
                _moduleAssociations.RemoveAll(asc => asc.Module == solvedModule);
                _selectedIndex = _moduleAssociations.IndexOf(asc => asc.Module == curSel);

                // Is there a house that is now losing out entirely?
                var missedHouse = Enumerable.Range(0, 4).IndexOf(house => !_points.ContainsKey((House) house) && !_moduleAssociations.Any(asc => asc.House == (House) house && !_specialModules.Contains(asc.Module)));
                if (missedHouse != -1)
                {
                    Debug.LogFormat(@"[Hogwarts #{0}] Strike because you solved all {1} modules unselected.", _moduleId, (House) missedHouse);
                    Module.HandleStrike();
                    _points[(House) missedHouse] = -1;
                }
            }
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
        Debug.LogFormat(@"[Hogwarts #{0}] Stage 2 activated. Correct answer{1}: {2}", _moduleId, houses.Length > 1 ? "s" : null, houses.JoinString(", "));
    }
}
