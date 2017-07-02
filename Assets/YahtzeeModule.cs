using System;
using System.Collections.Generic;
using System.Linq;
using Yahtzee;
using UnityEngine;
using Rnd = UnityEngine.Random;
using System.Collections;

/// <summary>
/// On the Subject of Yahtzee
/// Created by Timwi
/// </summary>
public class YahtzeeModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMAudio Audio;

    enum DiceColor
    {
        White = 0,
        Purple = 1,
        Blue = 2,
        Yellow = 3,
        Black = 4
    }
    public GameObject[] Dice;
    public KMSelectable[] DiceParent;

    private Vector3[] _diceLocations;
    private int[] _diceValues;
    private int?[] _keptDiceSlot;
    private bool[] _wasKept;
    private int _lastRolled;
    private bool _isSolved;
    private Coroutine[] _coroutines;

    public KMSelectable RollButton;

    private static Vector3[] _restingPlaces = new[] { new Vector3(.06f, .026f, .02f), new Vector3(.06f, .026f, -.005f), new Vector3(.06f, .026f, -.03f), new Vector3(.06f, .026f, -.055f) };
    private static Quaternion[] _rotations = new[] { Quaternion.Euler(0, 0, 0), Quaternion.Euler(90, 0, 0), Quaternion.Euler(0, 0, 90), Quaternion.Euler(0, 0, 270), Quaternion.Euler(270, 0, 0), Quaternion.Euler(180, 0, 0) };

    private static int _moduleIdCounter = 1;
    private int _moduleId;

    void Start()
    {
        _moduleId = _moduleIdCounter++;
        _keptDiceSlot = new int?[Dice.Length];
        _wasKept = new bool[Dice.Length];
        _diceValues = new int[Dice.Length];
        _diceLocations = new Vector3[Dice.Length];
        _coroutines = new Coroutine[Dice.Length];

        foreach (var dice in DiceParent)
            dice.gameObject.SetActive(false);

        RollButton.OnInteract = delegate
        {
            RollButton.AddInteractionPunch();
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonPress, RollButton.transform);

            if (_isSolved)
                return false;

            var numKeeping = 0;
            var numRolling = 0;

            // First roll ever?
            if (_lastRolled > 0)
            {
                Debug.LogFormat("[Yahtzee #{0}] Attempting to keep {1}.", _moduleId, Enumerable.Range(0, Dice.Length).Where(ix => _keptDiceSlot[ix] != null).Select(ix => string.Format("{0} ({1})", _diceValues[ix], (DiceColor) ix)).JoinString(", "));

                // Trying to keep dice of different values is always invalid
                int? keptValue = null;
                for (int i = 0; i < Dice.Length; i++)
                {
                    if (_keptDiceSlot[i] != null)
                    {
                        numKeeping++;
                        if (keptValue == null)
                            keptValue = _diceValues[i];
                        else if (_diceValues[i] != keptValue.Value)
                        {
                            Debug.LogFormat("[Yahtzee #{0}] Keeping dice of different values is not allowed. Strike.", _moduleId);
                            Module.HandleStrike();
                            return false;
                        }
                    }
                    else
                        numRolling++;
                }

                // Check if roll is legal
                switch (_lastRolled)
                {
                    case 5:
                        int? validValue;

                        // Large straight?
                        if (_diceValues.Contains(2) && _diceValues.Contains(3) && _diceValues.Contains(4) && _diceValues.Contains(5) && (_diceValues.Contains(1) || _diceValues.Contains(6)))
                        {
                            var eligibleValues = Bomb.GetSerialNumberNumbers().Where(_diceValues.Contains);
                            if (eligibleValues.Any())
                            {
                                validValue = eligibleValues.Max();
                                Debug.LogFormat("[Yahtzee #{0}] Large straight. Serial number contains {1}. Must keep {2}.", _moduleId, eligibleValues.JoinString(", "), validValue);
                            }
                            else
                            {
                                validValue = _diceValues[(int) DiceColor.Purple];
                                Debug.LogFormat("[Yahtzee #{0}] Large straight. Serial number contains no match. Must keep value of purple, which is {1}.", _moduleId, validValue);
                            }
                        }
                        // Small straight?
                        else if (_diceValues.Contains(3) && _diceValues.Contains(4) && (
                            (_diceValues.Contains(1) && _diceValues.Contains(2)) ||
                            (_diceValues.Contains(2) && _diceValues.Contains(5)) ||
                            (_diceValues.Contains(5) && _diceValues.Contains(6))))
                        {
                            var numbers = new List<int>(_diceValues);
                            numbers.Remove(3);
                            numbers.Remove(4);
                            // This code will remove EITHER a 5 OR a 1. It can’t remove both because then it would be a Large Straight, which we already checked for before.
                            numbers.Remove(5);
                            numbers.Remove(numbers.Remove(2) ? 1 : 6);  // The same is true of 2 and 6, of course.
                            validValue = numbers[0];
                            Debug.LogFormat("[Yahtzee #{0}] Small straight. Must keep outlier, which is {1}.", _moduleId, validValue);
                        }
                        // Three of a kind (includes full house), but not four of a kind
                        else if (Enumerable.Range(1, 6).Any(i => _diceValues.Count(val => val == i) == 3))
                        {
                            if (Bomb.GetOnIndicators().Count() >= 2)
                            {
                                validValue = _diceValues[(int) DiceColor.White];
                                Debug.LogFormat("[Yahtzee #{0}] Three of a kind and ≥ 2 lit indicators. Must keep value of white, which is {1}.", _moduleId, validValue);
                            }
                            else if (Bomb.GetOffIndicators().Count() >= 2)
                            {
                                validValue = _diceValues[(int) DiceColor.Black];
                                Debug.LogFormat("[Yahtzee #{0}] Three of a kind and ≥ 2 unlit indicators. Must keep value of black, which is {1}.", _moduleId, validValue);
                            }
                            else
                            {
                                validValue = Enumerable.Range(1, 6).Where(i => _diceValues.Count(val => val == i) < 3).Max();
                                Debug.LogFormat("[Yahtzee #{0}] Three of a kind and not enough indicators. Must keep highest value not in triplet, which is {1}.", _moduleId, validValue);
                            }
                        }
                        // Four of a kind or two pairs
                        else if (Enumerable.Range(1, 6).Any(i => _diceValues.Count(val => val == i) == 4) || Enumerable.Range(1, 6).Count(i => _diceValues.Count(val => val == i) == 2) == 2)
                        {
                            var batteryCount = Bomb.GetBatteryCount();
                            var batteryHolderCount = Bomb.GetBatteryHolderCount();
                            if (_diceValues.Contains(batteryCount))
                            {
                                validValue = batteryCount;
                                Debug.LogFormat("[Yahtzee #{0}] Four of a kind/two pairs and battery count matches. Must keep {1}.", _moduleId, validValue);
                            }
                            else if (_diceValues.Contains(batteryHolderCount))
                            {
                                validValue = batteryHolderCount;
                                Debug.LogFormat("[Yahtzee #{0}] Four of a kind/two pairs and battery holder count matches. Must keep {1}.", _moduleId, validValue);
                            }
                            else
                            {
                                validValue = _diceValues[(int) DiceColor.Yellow];
                                Debug.LogFormat("[Yahtzee #{0}] Four of a kind/two pairs and no battery/battery holder count match. Must keep value of yellow, which is {1}.", _moduleId, validValue);
                            }
                        }
                        // Pair
                        else if (Enumerable.Range(1, 6).Any(i => _diceValues.Count(val => val == i) == 2))
                        {
                            if (Bomb.IsPortPresent(KMBombInfoExtensions.KnownPortType.Parallel))
                            {
                                validValue = _diceValues[(int) DiceColor.Purple];
                                Debug.LogFormat("[Yahtzee #{0}] Pair and parallel port. Must keep value of purple, which is {1}.", _moduleId, validValue);
                            }
                            else if (Bomb.IsPortPresent(KMBombInfoExtensions.KnownPortType.PS2))
                            {
                                validValue = _diceValues[(int) DiceColor.Blue];
                                Debug.LogFormat("[Yahtzee #{0}] Pair and PS/2 port. Must keep value of blue, which is {1}.", _moduleId, validValue);
                            }
                            else if (Bomb.IsPortPresent(KMBombInfoExtensions.KnownPortType.StereoRCA))
                            {
                                validValue = _diceValues[(int) DiceColor.White];
                                Debug.LogFormat("[Yahtzee #{0}] Pair and stereo RCA port. Must keep value of white, which is {1}.", _moduleId, validValue);
                            }
                            else if (Bomb.IsPortPresent(KMBombInfoExtensions.KnownPortType.RJ45))
                            {
                                validValue = _diceValues[(int) DiceColor.Black];
                                Debug.LogFormat("[Yahtzee #{0}] Pair and RJ-45 port. Must keep value of black, which is {1}.", _moduleId, validValue);
                            }
                            else
                            {
                                validValue = _diceValues[(int) DiceColor.Yellow];
                                Debug.LogFormat("[Yahtzee #{0}] Pair and no matching port. Must keep value of yellow, which is {1}.", _moduleId, validValue);
                            }
                        }
                        else
                        {
                            // Otherwise: must roll all again
                            validValue = null;
                        }

                        if (validValue != keptValue)
                        {
                            Debug.LogFormat("[Yahtzee #{0}] You tried to keep {1}. Strike.", _moduleId, keptValue == null ? "nothing" : "a " + keptValue.Value);
                            Module.HandleStrike();
                            return false;
                        }
                        break;

                    case 4:
                        // Straight (small or large)?
                        if (_diceValues.Contains(3) && _diceValues.Contains(4) && (
                            (_diceValues.Contains(1) && _diceValues.Contains(2)) ||
                            (_diceValues.Contains(2) && _diceValues.Contains(5)) ||
                            (_diceValues.Contains(5) && _diceValues.Contains(6))))
                        {
                            // Must keep a value different from before
                            var prevKeptValue = _diceValues[Array.IndexOf(_wasKept, true)];
                            Debug.LogFormat("[Yahtzee #{0}] Straight. Must keep a value different from before.", _moduleId);
                            if (prevKeptValue == keptValue || keptValue == null)
                            {
                                Debug.LogFormat("[Yahtzee #{0}] You tried to keep {1}. Strike.", _moduleId, keptValue == null ? "nothing" : "a " + keptValue.Value);
                                Module.HandleStrike();
                                return false;
                            }
                        }
                        // Keep 1 only allowed if it isn’t black (or it’s in the serial)
                        else if (numKeeping == 1 && _keptDiceSlot[(int) DiceColor.Black] != null && !Bomb.GetSerialNumberNumbers().Contains(keptValue.Value))
                        {
                            Debug.LogFormat("[Yahtzee #{0}] Keeping 1 dice only allowed if its value is in the serial number, or it’s not black. Strike.", _moduleId);
                            Module.HandleStrike();
                            return false;
                        }
                        // Keep 2 only allowed if neither is blue (or it’s in the serial)
                        else if (numKeeping == 2 && _keptDiceSlot[(int) DiceColor.Blue] != null && !Bomb.GetSerialNumberNumbers().Contains(keptValue.Value))
                        {
                            Debug.LogFormat("[Yahtzee #{0}] Keeping 2 dice only allowed if their value is in the serial number, or neither is blue. Strike.", _moduleId);
                            Module.HandleStrike();
                            return false;
                        }
                        // Not allowed to keep number of dice equal to number of port plates
                        else if (numKeeping >= 3 && numKeeping == Bomb.GetPortPlateCount())
                        {
                            Debug.LogFormat("[Yahtzee #{0}] Keeping {1} dice is not allowed because there are exactly {1} port plates. Strike.", _moduleId, numKeeping);
                            Module.HandleStrike();
                            return false;
                        }
                        // Keep 3 allowed if the other two both aren’t in the serial number
                        else if (numKeeping == 3 && Enumerable.Range(0, Dice.Length).Any(ix => _keptDiceSlot[ix] == null && Bomb.GetSerialNumberNumbers().Contains(_diceValues[ix])))
                        {
                            Debug.LogFormat("[Yahtzee #{0}] Keeping 3 dice only allowed if their value is in the serial number, or neither of the remaining two is in the serial number. Strike.", _moduleId);
                            Module.HandleStrike();
                            return false;
                        }
                        // Keep 4 allowed if the fifth is bigger
                        else if (numKeeping == 4 && _diceValues[Array.IndexOf(_keptDiceSlot, null)] <= keptValue.Value)
                        {
                            Debug.LogFormat("[Yahtzee #{0}] Keeping 4 dice only allowed if the fifth one is bigger. Strike.", _moduleId);
                            Module.HandleStrike();
                            return false;
                        }
                        break;

                    case 3:
                        // Full house
                        if (Enumerable.Range(1, 6).Any(i => _diceValues.Count(val => val == i) == 3) && Enumerable.Range(1, 6).Any(i => _diceValues.Count(val => val == i) == 2))
                        {
                            var duplicatePorts = Bomb.GetPortCount() > Bomb.GetPorts().Distinct().Count();
                            if (duplicatePorts && numKeeping != 3)
                            {
                                Debug.LogFormat("[Yahtzee #{0}] Full house and duplicate port. Must reroll the pair. Strike.", _moduleId);
                                Module.HandleStrike();
                                return false;
                            }
                            else if (duplicatePorts && (numKeeping != 2 || Enumerable.Range(0, Dice.Length).Where(ix => _keptDiceSlot[ix] == null).Select(ix => _diceValues[ix]).Distinct().Count() != 1))
                            {
                                Debug.LogFormat("[Yahtzee #{0}] Full house and no duplicate port. Must reroll the triplet. Strike.", _moduleId);
                                Module.HandleStrike();
                                return false;
                            }
                        }
                        // Keep 2 always allowed
                        // Any number of keeps allowed if the kept value is in the serial
                        else if (numKeeping == 2 || (keptValue != null && Bomb.GetSerialNumberNumbers().Contains(keptValue.Value)))
                        {
                        }
                        // Keep 3 allowed if purple or white was kept in the previous stage
                        else if (numKeeping == 3 && !_wasKept[(int) DiceColor.Purple] && !_wasKept[(int) DiceColor.Blue])
                        {
                            Debug.LogFormat("[Yahtzee #{0}] Keeping 3 only allowed if purple or white was kept in the previous stage. Strike.", _moduleId);
                            Module.HandleStrike();
                            return false;
                        }
                        // Keep 4 allowed if the fifth is smaller
                        else if (numKeeping == 4 && _diceValues[Array.IndexOf(_keptDiceSlot, null)] >= keptValue)
                        {
                            Debug.LogFormat("[Yahtzee #{0}] Keeping 4 only allowed if the fifth value is smaller. Strike.", _moduleId);
                            Module.HandleStrike();
                            return false;
                        }
                        break;

                    case 2:
                        // Keep 4 allowed if yellow was kept in the previous stage, or if fifth is 1 away in value from kept value
                        if (numKeeping == 4 && !_wasKept[(int) DiceColor.Yellow] && !_wasKept[(int) DiceColor.Blue] && _diceValues[Array.IndexOf(_keptDiceSlot, null)] != keptValue.Value - 1 && _diceValues[Array.IndexOf(_keptDiceSlot, null)] != keptValue.Value + 1)
                        {
                            Debug.LogFormat("[Yahtzee #{0}] Keeping 4 only allowed if yellow or blue was kept in the previous stage, or if fifth is 1 away in value from kept value. Strike.", _moduleId);
                            Module.HandleStrike();
                            return false;
                        }
                        break;
                }

                Debug.LogFormat("[Yahtzee #{0}] Keeping {1} and rerolling {2} dice.",
                    _moduleId,
                    numKeeping == 0 ? "nothing" : string.Format("{0} ({1})", Enumerable.Range(0, Dice.Length).Where(ix => _keptDiceSlot[ix] != null).Select(ix => (DiceColor) ix).JoinString(", ", lastSeparator: " and "), keptValue.Value),
                    numRolling);
            }
            else
                Debug.LogFormat("[Yahtzee #{0}] Rolling all 5 dice.", _moduleId);

            for (int i = 0; i < Dice.Length; i++)
            {
                if (_keptDiceSlot[i] == null)
                {
                    _diceValues[i] = Rnd.Range(1, 7);
                    Debug.LogFormat("[Yahtzee #{0}] {1} is now a {2}.", _moduleId, (DiceColor) i, _diceValues[i]);
                }

                var iterations = 0;
                do { _diceLocations[i] = new Vector3(Rnd.Range(-.063f, .019f), .025f, Rnd.Range(-.069f, .028f)); }
                while (_diceLocations.Where((loc, ix) => ix < i && (loc - _diceLocations[i]).magnitude < .03f).Any() && ++iterations < 1000);
                _wasKept[i] = _keptDiceSlot != null;
            }

            var sorted = Enumerable.Range(0, Dice.Length).Where(ix => _keptDiceSlot[ix] == null).OrderBy(ix => _diceLocations[ix].z).ToArray();
            for (int i = 0; i < sorted.Length; i++)
            {
                if (_coroutines[sorted[i]] != null)
                    StopCoroutine(_coroutines[sorted[i]]);
                _coroutines[sorted[i]] = StartCoroutine(rollDice(new Vector3(-.1f, .1f, -.069f + .1f * i / sorted.Length), sorted[i]));
            }

            _lastRolled = _keptDiceSlot.Count(kept => kept == null);
            if (_diceValues.Distinct().Count() == 1)
            {
                _isSolved = true;
                Debug.LogFormat("[Yahtzee #{0}] Yahtzee. Module solved.", _moduleId);
                StartCoroutine(victory());
            }
            return false;
        };

        for (int i = 0; i < DiceParent.Length; i++)
            DiceParent[i].OnInteract = getDiceHandler(i);
    }

    private IEnumerator victory()
    {
        yield return new WaitForSeconds(1f);
        Module.HandlePass();
    }

    private KMSelectable.OnInteractHandler getDiceHandler(int i)
    {
        return delegate
        {
            if (_isSolved)
                return false;

            if (_coroutines[i] != null)
                StopCoroutine(_coroutines[i]);

            if (_keptDiceSlot[i] == null)
            {
                var firstFreeSlot = Enumerable.Range(0, _restingPlaces.Length + 1).First(ix => ix == _restingPlaces.Length || !_keptDiceSlot.Contains(ix));
                if (firstFreeSlot == _restingPlaces.Length)
                    // The user tried to keep the fifth dice. Just disallow that.
                    return false;
                _keptDiceSlot[i] = firstFreeSlot;
                _coroutines[i] = StartCoroutine(moveDice(i,
                    startParentRotation: DiceParent[i].transform.localRotation,
                    endParentRotation: Quaternion.Euler(0, 0, 0),
                    startDiceRotation: Dice[i].transform.localRotation,
                    endDiceRotation: _rotations[_diceValues[i] - 1],
                    startLocation: DiceParent[i].transform.localPosition,
                    endLocation: _restingPlaces[firstFreeSlot]));
            }
            else
            {
                _keptDiceSlot[i] = null;
                _coroutines[i] = StartCoroutine(moveDice(i,
                    startParentRotation: DiceParent[i].transform.localRotation,
                    endParentRotation: Quaternion.Euler(0, Rnd.Range(0, 360), 0),
                    startDiceRotation: Dice[i].transform.localRotation,
                    endDiceRotation: _rotations[_diceValues[i] - 1],
                    startLocation: DiceParent[i].transform.localPosition,
                    endLocation: _diceLocations[i]));
            }

            return false;
        };
    }

    private float easeOutSine(float time, float duration, float from, float to)
    {
        return (to - from) * Mathf.Sin(time / duration * (Mathf.PI / 2)) + from;
    }

    private IEnumerator rollDice(Vector3 startLocation, int ix)
    {
        return moveDice(ix,
            startParentRotation: Quaternion.Euler(Rnd.Range(0, 360), Rnd.Range(0, 360), Rnd.Range(0, 360)),
            endParentRotation: Quaternion.Euler(0, Rnd.Range(0, 360), 0),
            startDiceRotation: Quaternion.Euler(Rnd.Range(0, 360), Rnd.Range(0, 360), Rnd.Range(0, 360)),
            endDiceRotation: _rotations[_diceValues[ix] - 1],
            startLocation: startLocation,
            endLocation: _diceLocations[ix],
            delay: true);
    }

    private IEnumerator moveDice(int ix, Quaternion startParentRotation, Quaternion endParentRotation, Quaternion startDiceRotation, Quaternion endDiceRotation, Vector3 startLocation, Vector3 endLocation, bool delay = false)
    {
        if (delay)
        {
            DiceParent[ix].gameObject.SetActive(false);
            yield return new WaitForSeconds((-_diceLocations[ix].x + .02f) * 5);
            DiceParent[ix].gameObject.SetActive(true);
        }

        var speed = Rnd.Range(1f, 2.2f);
        for (float n = 0; n < 1; n += speed * Time.deltaTime)
        {
            DiceParent[ix].transform.localPosition = Vector3.Lerp(startLocation, endLocation, easeOutSine(n, 1, 0, 1));
            Dice[ix].transform.localRotation = Quaternion.Slerp(startDiceRotation, endDiceRotation, easeOutSine(n, 1, 0, 1));
            DiceParent[ix].transform.localRotation = Quaternion.Slerp(startParentRotation, endParentRotation, easeOutSine(n, 1, 0, 1));
            yield return null;
        }

        DiceParent[ix].transform.localPosition = endLocation;
        Dice[ix].transform.localRotation = endDiceRotation;
        DiceParent[ix].transform.localRotation = endParentRotation;
        _coroutines[ix] = null;
    }
}