using Saltline.Core;
using Saltline.FX;
using Saltline.Weapons;
using UnityEngine;

namespace Saltline.UI
{
    /// <summary>
    /// The menu/lobby background: the five weapons on a slowly turning display stand, lit
    /// like a studio shot. Built at runtime from the same greybox weapon builder the game uses.
    /// </summary>
    public sealed class MenuBackdrop : MonoBehaviour
    {
        [SerializeField] float turnSpeed = 6f;
        Transform m_Stand;

        void Start()
        {
            if (!RunMode.Presentation) return;
            m_Stand = new GameObject("Stand").transform;
            m_Stand.SetParent(transform, false);

            WeaponClass[] classes = { WeaponClass.AssaultRifle, WeaponClass.SMG, WeaponClass.Shotgun, WeaponClass.Sniper, WeaponClass.Pistol };
            for (int i = 0; i < classes.Length; i++)
            {
                var holder = new GameObject(classes[i].ToString()).transform;
                holder.SetParent(m_Stand, false);
                float a = i * Mathf.PI * 2f / classes.Length;
                holder.localPosition = new Vector3(Mathf.Sin(a) * 0.95f, 0.15f + 0.04f * i, Mathf.Cos(a) * 0.95f);
                holder.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg + 90f, -8f);
                holder.localScale = Vector3.one * 2.2f;
                GreyboxGun.Build(classes[i], holder, 0, out _);
            }
        }

        void Update()
        {
            if (m_Stand != null) m_Stand.Rotate(0f, turnSpeed * Time.unscaledDeltaTime, 0f, Space.Self);
        }
    }
}
