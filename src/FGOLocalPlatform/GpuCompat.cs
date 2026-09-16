using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace FGOLocalPlatform;

/// <summary>
/// The OpenGL compatibility layer for AMD and Intel graphics. The game asks for NVIDIA-only
/// extensions, and a layer in App\ translates them. Two layers exist, and the package ships both:
/// fluphus's shim under compat\amd-shim, installed as App\opengl32.dll beside a copy of the system
/// DLL and a driver profile, and the older fgoglcompat.dll under compat\. Whatever file sits under
/// compat\ is what gets installed, so a newer build of either layer only has to be dropped there.
/// An install keeps whichever layer it has; nothing here changes a layer except the switch on the
/// Display page, which is the player's to use, and the first run of a fresh install without an
/// NVIDIA card.
/// A fresh install without an NVIDIA card gets the older layer, which is the one that works on the
/// AMD cards players report; the newer one is a click away on the Display page.
/// </summary>
internal static class GpuCompat
{
	internal enum Layer
	{
		None,
		Legacy,
		Shim,
		/// <summary>An App\opengl32.dll that is not the copy under compat\amd-shim: the shim's own installer put it there, or the compat copy has since been replaced by a newer build.</summary>
		Foreign
	}

	private const string LegacyFileName = "fgoglcompat.dll";

	private const string DisplayClassKey = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}";

	private static string CompatRoot => Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "compat"));

	public static string LegacySourcePath => Path.Combine(CompatRoot, LegacyFileName);

	public static string ShimSourcePath => Path.Combine(CompatRoot, "amd-shim", "opengl32.dll");

	private static string ShimConfigSourcePath => Path.Combine(CompatRoot, "amd-shim", "amdcfg", "amdOglpSettings.cfg");

	private static string LegacyTargetPath => Path.Combine(GamePaths.GameRoot, LegacyFileName);

	private static string ShimTargetPath => Path.Combine(GamePaths.GameRoot, "opengl32.dll");

	private static string ShimRealTargetPath => Path.Combine(GamePaths.GameRoot, "opengl32real.dll");

	private static string ShimConfigTargetPath => Path.Combine(GamePaths.GameRoot, "amdcfg", "amdOglpSettings.cfg");

	public static bool LegacySourceAvailable => File.Exists(LegacySourcePath);

	public static bool ShimSourceAvailable => File.Exists(ShimSourcePath) && File.Exists(ShimConfigSourcePath);

	public static bool SourceAvailable => ShimSourceAvailable || LegacySourceAvailable;

	/// <summary>
	/// Copies of the older layer that do nothing where they are: next to the launcher, or in
	/// App\ beside a shim, where the game would load both layers at once.
	/// </summary>
	public static List<string> StrayLegacyCopies()
	{
		List<string> list = new List<string>();
		string root = Path.Combine(Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..")), LegacyFileName);
		if (File.Exists(root))
		{
			list.Add(root);
		}
		if (File.Exists(LegacyTargetPath) && File.Exists(ShimTargetPath))
		{
			list.Add(LegacyTargetPath);
		}
		return list;
	}

	/// <summary>The layer in App\ right now. The shim counts as installed when App\opengl32.dll is the copy under compat\amd-shim; any other App\opengl32.dll is Foreign.</summary>
	public static Layer Installed
	{
		get
		{
			if (File.Exists(ShimTargetPath))
			{
				return SameFile(ShimTargetPath, ShimSourcePath) ? Layer.Shim : Layer.Foreign;
			}
			return File.Exists(LegacyTargetPath) ? Layer.Legacy : Layer.None;
		}
	}

	/// <summary>True when both files exist and hold the same bytes.</summary>
	private static bool SameFile(string a, string b)
	{
		return File.Exists(a) && File.Exists(b) && new FileInfo(a).Length == new FileInfo(b).Length && string.Equals(Updater.HashFile(a), Updater.HashFile(b), StringComparison.OrdinalIgnoreCase);
	}

	public static bool IsInstalled => Installed != Layer.None;

	/// <summary>
	/// Turns the layer on or off. On keeps the layer the install already has and gives a fresh
	/// install the older layer, or the shim when compat\ has no older layer; off removes what is
	/// there, a foreign opengl32.dll included, since the switch is only ever moved by the player.
	/// Throws on an I/O failure so the caller can say so.
	/// </summary>
	public static void Apply(bool enabled)
	{
		switch (Installed)
		{
		case Layer.Shim:
		case Layer.Foreign:
			if (!enabled)
			{
				RemoveShim();
			}
			return;
		case Layer.Legacy:
			if (!enabled)
			{
				File.Delete(LegacyTargetPath);
			}
			else if (new FileInfo(LegacyTargetPath).Length != new FileInfo(LegacySourcePath).Length)
			{
				File.Copy(LegacySourcePath, LegacyTargetPath, overwrite: true);
			}
			return;
		default:
			if (!enabled)
			{
				return;
			}
			if (LegacySourceAvailable)
			{
				File.Copy(LegacySourcePath, LegacyTargetPath, overwrite: true);
			}
			else
			{
				InstallShim();
			}
			return;
		}
	}

	/// <summary>Replaces the older layer with the shim. The older layer's file stays under compat\ for the way back.</summary>
	public static void SwitchToShim()
	{
		if (File.Exists(LegacyTargetPath))
		{
			File.Delete(LegacyTargetPath);
		}
		InstallShim();
	}

	/// <summary>Replaces the shim with the older layer this install had before.</summary>
	public static void SwitchToLegacy()
	{
		RemoveShim();
		File.Copy(LegacySourcePath, LegacyTargetPath, overwrite: true);
	}

	/// <summary>
	/// True when the compat folder holds a different build of the layer that is installed: the
	/// player dropped a newer file there, or App\ holds a copy the launcher did not put there.
	/// </summary>
	public static bool CompatCopyDiffers
	{
		get
		{
			switch (Installed)
			{
			case Layer.Foreign:
				return ShimSourceAvailable;
			case Layer.Legacy:
				return LegacySourceAvailable && !SameFile(LegacyTargetPath, LegacySourcePath);
			default:
				return false;
			}
		}
	}

	/// <summary>Installs the copy under compat\ over the layer of the same kind in App\.</summary>
	public static void InstallFromCompat()
	{
		if (Installed == Layer.Legacy)
		{
			File.Copy(LegacySourcePath, LegacyTargetPath, overwrite: true);
		}
		else
		{
			InstallShim();
		}
	}

	/// <summary>
	/// The three files fluphus's installer writes: the system OpenGL DLL under the name the shim
	/// forwards to, the shim itself, and the driver profile.
	/// </summary>
	private static void InstallShim()
	{
		File.Copy(Path.Combine(Environment.SystemDirectory, "opengl32.dll"), ShimRealTargetPath, overwrite: true);
		File.Copy(ShimSourcePath, ShimTargetPath, overwrite: true);
		Directory.CreateDirectory(Path.GetDirectoryName(ShimConfigTargetPath));
		File.Copy(ShimConfigSourcePath, ShimConfigTargetPath, overwrite: true);
	}

	private static void RemoveShim()
	{
		foreach (string path in new string[3] { ShimTargetPath, ShimRealTargetPath, ShimConfigTargetPath })
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		string configFolder = Path.GetDirectoryName(ShimConfigTargetPath);
		if (Directory.Exists(configFolder) && Directory.GetFileSystemEntries(configFolder).Length == 0)
		{
			Directory.Delete(configFolder);
		}
	}

	/// <summary>
	/// True when any display adapter is NVIDIA. A laptop with NVIDIA beside an Intel chip counts as
	/// NVIDIA, since the hook already asks Windows for the NVIDIA card. Unreadable registry counts as
	/// NVIDIA too, so nothing is installed by guesswork.
	/// </summary>
	public static bool HasNvidiaAdapter()
	{
		try
		{
			using RegistryKey? displayClass = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
			if (displayClass == null)
			{
				return true;
			}
			bool sawAdapter = false;
			foreach (string name in displayClass.GetSubKeyNames())
			{
				if (name.Length != 4 || !int.TryParse(name, out _))
				{
					continue;
				}
				using RegistryKey? adapter = displayClass.OpenSubKey(name);
				if (adapter == null)
				{
					continue;
				}
				string provider = adapter.GetValue("ProviderName") as string ?? "";
				string description = adapter.GetValue("DriverDesc") as string ?? "";
				if (provider.Length == 0 && description.Length == 0)
				{
					continue;
				}
				sawAdapter = true;
				if (provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || description.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return !sawAdapter;
		}
		catch (Exception)
		{
			return true;
		}
	}
}
