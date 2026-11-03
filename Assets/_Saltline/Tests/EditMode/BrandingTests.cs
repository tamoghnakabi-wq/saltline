using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Saltline.Tests
{
    /// The app icon and the menu logo, drawn by ArtSource/Branding/make_logo.py and assigned by Branding.cs.
    /// 0.1.0 shipped with no icon at all, because none had been assigned.
    public class BrandingTests
    {
        const string Folder = "Assets/_Saltline/Art/Branding/";

        [Test]
        public void EveryDesktopIconSizeHasTheSaltlineIcon()
        {
            var sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any);
            var icons = PlayerSettings.GetIcons(NamedBuildTarget.Standalone, IconKind.Any);
            Assert.That(sizes, Is.Not.Empty);
            Assert.That(icons.Length, Is.EqualTo(sizes.Length));
            for (int i = 0; i < sizes.Length; i++)
            {
                Assert.That(icons[i], Is.Not.Null, $"no icon for {sizes[i]} px");
                Assert.That(AssetDatabase.GetAssetPath(icons[i]), Does.StartWith(Folder + "SaltlineIcon_"), $"{sizes[i]} px");
                Assert.That(icons[i].width, Is.GreaterThanOrEqualTo(sizes[i]), $"{sizes[i]} px icon is smaller than its slot");
            }
        }

        [Test]
        public void DefaultIconIsTheLargestSaltlineIcon()
        {
            var icons = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            Assert.That(icons, Is.Not.Empty);
            Assert.That(AssetDatabase.GetAssetPath(icons[0]), Is.EqualTo(Folder + "SaltlineIcon_1024.png"));
        }

        [Test]
        public void MenuShowsTheLogoMark()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "SaltlineMark.png"), Is.Not.Null);
            string uss = File.ReadAllText("Assets/_Saltline/UI/Saltline.uss");
            Assert.That(uss, Does.Contain("url(\"project://database/" + Folder + "SaltlineMark.png\")"));
        }
    }
}
