using System;
using System.Collections.Generic;
using System.Linq;
using Yahtzee;
using UnityEngine;
using Rnd = UnityEngine.Random;

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
        Red = 1,
        Blue = 2,
        Yellow = 3,
        Black = 4
    }
    public KMSelectable[] Dice;
    public KMSelectable RollButton;

    private static Vector3[] _restingPlaces = new[] { new Vector3(.06f, .026f, .02f), new Vector3(.06f, .026f, -.005f), new Vector3(.06f, .026f, -.03f), new Vector3(.06f, .026f, -.055f) };

    private static int _moduleIdCounter = 1;
    private int _moduleId;

    void Start()
    {
        _moduleId = _moduleIdCounter++;
        RollButton.OnInteract = delegate
        {
            Debug.Log("ROLL CLICKED");
            return false;
        };
    }
}
