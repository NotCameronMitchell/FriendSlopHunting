using System.Collections.Generic;
using FriendSlop.Animals;
using FriendSlop.Game;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>
    /// The trader's drop-off area. Drop or throw a carcass in and the host sells it for the team.
    /// Needs a trigger collider on this object. Selling only happens on the host.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SellZone : MonoBehaviour
    {
        readonly HashSet<Animal> _sold = new HashSet<Animal>();

        void Reset() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerStay(Collider other)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;

            var animal = other.GetComponentInParent<Animal>();
            if (animal == null || !animal.IsSpawned || !animal.IsDead.Value || animal.Carry.IsHeld) return;
            if (!_sold.Add(animal)) return; // several colliders per animal: sell once

            int value = animal.SaleValue;
            string style = animal.StyleDescription;
            string line = $"SOLD {animal.Species.displayName} ({animal.WeightKg.Value:0.0} kg) +${value}";
            if (!string.IsNullOrEmpty(style)) line += $"  [{style}]";

            GameState.Instance?.ServerAddMoney(value, line);
            animal.NetworkObject.Despawn(true);
        }

        void LateUpdate() => _sold.RemoveWhere(a => a == null);
    }
}
