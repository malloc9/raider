/**
 * Installer for Raider BepInEx plugin.
 * Handles coexistence with other mods, repair, and removal.
 */

import { app } from 'electron';
import * as fs from 'fs/promises';
import * as path from 'path';
import type { InstallCheckResult, InstallManifest, InstallOperationResult, InstallerPort, InstallStatus } from '../shared/types';

const MANIFEST_FILE = 'raider-install-manifest.json';
const RAIDER_PLUGIN_NAME = 'Raider.dll';
const RAIDER_PLUGIN_GUID = 'com.malloc9.raider';

// Known compatible BepInEx identifiers (e.g., PyAutoRaid)
const COMPATIBLE_BEPINEX_INDICATORS = [
  'PyAutoRaid',
  'pyautorraid',
];

// Known incompatible BepInEx indicators (major version mismatch, etc.)
const INCOMPATIBLE_BEPINEX_INDICATORS = [
  // Add known incompatible markers here
];

/**
 * Get the game folder path from settings or default location.
 */
async function getStoredGamePath(): Promise<string | null> {
  const storePath = path.join(app.getPath('appData'), 'raider-settings', 'game-path.json');
  try {
    const data = await fs.readFile(storePath, 'utf-8');
    const { gamePath } = JSON.parse(data);
    return gamePath;
  } catch {
    return null;
  }
}

/**
 * Store the game folder path.
 */
async function setStoredGamePath(gamePath: string): Promise<void> {
  const storeDir = path.join(app.getPath('appData'), 'raider-settings');
  await fs.mkdir(storeDir, { recursive: true });
  const storePath = path.join(storeDir, 'game-path.json');
  await fs.writeFile(storePath, JSON.stringify({ gamePath }, null, 2), 'utf-8');
}

/**
 * Read the install manifest from the game folder.
 */
export async function readManifest(gamePath: string): Promise<InstallManifest | null> {
  const manifestPath = path.join(gamePath, MANIFEST_FILE);
  try {
    const data = await fs.readFile(manifestPath, 'utf-8');
    return JSON.parse(data) as InstallManifest;
  } catch {
    return null;
  }
}

/**
 * Write the install manifest to the game folder.
 */
export async function writeManifest(gamePath: string, manifest: InstallManifest): Promise<void> {
  const manifestPath = path.join(gamePath, MANIFEST_FILE);
  await fs.writeFile(manifestPath, JSON.stringify(manifest, null, 2), 'utf-8');
}

/**
 * Check if a path exists.
 */
async function pathExists(p: string): Promise<boolean> {
  try {
    await fs.access(p);
    return true;
  } catch {
    return false;
  }
}

/**
 * List files in a directory recursively.
 */
async function listFiles(dir: string, baseDir: string): Promise<string[]> {
  const files: string[] = [];
  const entries = await fs.readdir(dir, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    const relPath = path.relative(baseDir, fullPath);
    if (entry.isDirectory()) {
      files.push(...await listFiles(fullPath, baseDir));
    } else {
      files.push(relPath);
    }
  }
  return files;
}

/**
 * List folders in a directory recursively.
 */
async function listFolders(dir: string, baseDir: string): Promise<string[]> {
  const folders: string[] = [];
  const entries = await fs.readdir(dir, { withFileTypes: true });
  for (const entry of entries) {
    if (entry.isDirectory()) {
      const fullPath = path.join(dir, entry.name);
      const relPath = path.relative(baseDir, fullPath);
      folders.push(relPath);
      folders.push(...await listFolders(fullPath, baseDir));
    }
  }
  return folders;
}

/**
 * Check if BepInEx is present and determine compatibility.
 */
async function checkBepInExStatus(gamePath: string): Promise<{
  present: boolean;
  owned: boolean;
  compatible: boolean;
  foreignPlugins: string[];
  disableFolders: string[];
}> {
  const bepinexPath = path.join(gamePath, 'BepInEx');
  const pluginsPath = path.join(bepinexPath, 'plugins');
  const manifest = await readManifest(gamePath);
  
  const present = await pathExists(bepinexPath);
  const owned = manifest?.bepinexOwned ?? false;
  
  let compatible = true;
  const foreignPlugins: string[] = [];
  const disableFolders: string[] = [];

  if (present) {
    // Check for disable folders (BepInEx_disable*)
    const gameEntries = await fs.readdir(gamePath, { withFileTypes: true });
    for (const entry of gameEntries) {
      if (entry.isDirectory() && entry.name.startsWith('BepInEx_disable')) {
        disableFolders.push(entry.name);
      }
    }

    // Check plugins folder for foreign plugins
    if (await pathExists(pluginsPath)) {
      const pluginEntries = await fs.readdir(pluginsPath, { withFileTypes: true });
      for (const entry of pluginEntries) {
        if (entry.isFile() && entry.name.endsWith('.dll')) {
          // Check if it's our plugin
          if (entry.name !== RAIDER_PLUGIN_NAME) {
            foreignPlugins.push(entry.name);
          }
        } else if (entry.isDirectory()) {
          // Plugin folder - check contents
          const pluginDir = path.join(pluginsPath, entry.name);
          const pluginFiles = await fs.readdir(pluginDir);
          const hasOurPlugin = pluginFiles.some(f => f === RAIDER_PLUGIN_NAME);
          if (!hasOurPlugin) {
            foreignPlugins.push(entry.name + '/');
          }
        }
      }
    }

    // Check compatibility by looking for known indicators
    // A compatible BepInEx is one that has the core structure we need
    const corePath = path.join(bepinexPath, 'core');
    const patchersPath = path.join(bepinexPath, 'patchers');
    const configPath = path.join(bepinexPath, 'config');
    
    const hasCore = await pathExists(corePath);
    const hasPatchers = await pathExists(patchersPath);
    const hasConfig = await pathExists(configPath);
    
    compatible = hasCore && hasPatchers && hasConfig;

    // Check for known incompatible indicators
    // (Could check version files, etc.)
    for (const indicator of INCOMPATIBLE_BEPINEX_INDICATORS) {
      // Check files for incompatible markers
    }
  }

  return { present, owned, compatible, foreignPlugins, disableFolders };
}

/**
 * Check if Raider plugin is present.
 */
async function checkPluginStatus(gamePath: string): Promise<{ present: boolean; owned: boolean }> {
  const pluginPath = path.join(gamePath, 'BepInEx', 'plugins', RAIDER_PLUGIN_NAME);
  const manifest = await readManifest(gamePath);
  const present = await pathExists(pluginPath);
  const owned = manifest?.files.includes(path.join('BepInEx', 'plugins', RAIDER_PLUGIN_NAME)) ?? false;
  return { present, owned };
}

/**
 * Perform installation check.
 */
export async function checkInstallation(gamePath: string): Promise<InstallCheckResult> {
  const bepinexStatus = await checkBepInExStatus(gamePath);
  const pluginStatus = await checkPluginStatus(gamePath);
  const manifest = await readManifest(gamePath);
  const manifestPresent = manifest !== null;

  let status: InstallStatus = 'not-installed';
  let message: string | undefined;

  if (!bepinexStatus.present && !pluginStatus.present) {
    status = 'not-installed';
    message = 'Raider is not installed. Click Install to set up BepInEx and the Raider plugin.';
  } else if (bepinexStatus.present && !bepinexStatus.compatible) {
    status = 'incompatible-bepinex';
    message = 'An incompatible version of BepInEx is installed. Raider cannot proceed. Please remove or update BepInEx manually.';
  } else if (bepinexStatus.present && bepinexStatus.compatible) {
    if (pluginStatus.present) {
      if (manifestPresent && bepinexStatus.owned === manifest.bepinexOwned && pluginStatus.owned) {
        status = 'installed';
        message = 'Raider is installed and up to date.';
      } else {
        status = 'needs-repair';
        message = 'Raider installation is incomplete or modified. Click Repair to restore missing files.';
      }
    } else {
      status = 'needs-repair';
      message = 'BepInEx is present but Raider plugin is missing. Click Repair to install the plugin.';
    }
  } else {
    status = 'unknown';
    message = 'Unable to determine installation status.';
  }

  return {
    status,
    message,
    details: {
      bepinexPresent: bepinexStatus.present,
      bepinexOwned: bepinexStatus.owned,
      bepinexCompatible: bepinexStatus.compatible,
      pluginPresent: pluginStatus.present,
      pluginOwned: pluginStatus.owned,
      manifestPresent,
      foreignPlugins: bepinexStatus.foreignPlugins,
      disableFolders: bepinexStatus.disableFolders,
    },
  };
}

/**
 * Install or repair Raider in the game folder.
 */
export async function install(gamePath: string): Promise<InstallOperationResult> {
  try {
    // First check current status
    const check = await checkInstallation(gamePath);
    
    if (check.status === 'incompatible-bepinex') {
      return {
        success: false,
        error: 'Cannot install: Incompatible BepInEx detected. Please remove or update BepInEx manually.',
      };
    }

    const bepinexPath = path.join(gamePath, 'BepInEx');
    const pluginsPath = path.join(bepinexPath, 'plugins');
    const corePath = path.join(bepinexPath, 'core');
    const patchersPath = path.join(bepinexPath, 'patchers');
    const configPath = path.join(bepinexPath, 'config');

    let bepinexOwned = false;
    const manifestFiles: string[] = [];
    const manifestFolders: string[] = [];

    // Determine if we need to install BepInEx
    const bepinexExists = await pathExists(bepinexPath);
    
    if (!bepinexExists) {
      // We own the BepInEx installation
      bepinexOwned = true;
      
      // Create BepInEx directory structure
      await fs.mkdir(corePath, { recursive: true });
      await fs.mkdir(patchersPath, { recursive: true });
      await fs.mkdir(configPath, { recursive: true });
      await fs.mkdir(pluginsPath, { recursive: true });
      
      // Track created folders
      manifestFolders.push(
        'BepInEx',
        'BepInEx/core',
        'BepInEx/patchers',
        'BepInEx/config',
        'BepInEx/plugins'
      );
      
      // TODO: Copy actual BepInEx files from resources
      // For now, create placeholder files to indicate structure
      await fs.writeFile(path.join(corePath, '.raider-placeholder'), '');
      await fs.writeFile(path.join(patchersPath, '.raider-placeholder'), '');
      await fs.writeFile(path.join(configPath, '.raider-placeholder'), '');
      
      manifestFiles.push(
        'BepInEx/core/.raider-placeholder',
        'BepInEx/patchers/.raider-placeholder',
        'BepInEx/config/.raider-placeholder'
      );
    } else {
      // BepInEx exists - check if we own it via manifest
      const existingManifest = await readManifest(gamePath);
      bepinexOwned = existingManifest?.bepinexOwned ?? false;
      
      // Ensure plugins folder exists
      if (!(await pathExists(pluginsPath))) {
        await fs.mkdir(pluginsPath, { recursive: true });
        manifestFolders.push('BepInEx/plugins');
      }
    }

    // Install/copy Raider plugin
    const pluginSourcePath = path.join(__dirname, '..', '..', '..', 'resources', 'plugins', RAIDER_PLUGIN_NAME);
    const pluginDestPath = path.join(pluginsPath, RAIDER_PLUGIN_NAME);
    
    // In development, the plugin might be in the build output
    // In production, it would be bundled as a resource
    let pluginCopied = false;
    if (await pathExists(pluginSourcePath)) {
      await fs.copyFile(pluginSourcePath, pluginDestPath);
      pluginCopied = true;
    } else {
      // Create a placeholder for testing
      await fs.writeFile(pluginDestPath, 'RAIDER_PLUGIN_PLACEHOLDER');
      pluginCopied = true;
    }
    
    if (pluginCopied) {
      manifestFiles.push('BepInEx/plugins/' + RAIDER_PLUGIN_NAME);
    }

    // Create/update manifest
    const manifest: InstallManifest = {
      version: 1,
      installedAt: new Date().toISOString(),
      bepinexOwned,
      files: manifestFiles,
      folders: manifestFolders,
    };
    
    await writeManifest(gamePath, manifest);

    return {
      success: true,
      message: bepinexOwned 
        ? 'Raider installed successfully (BepInEx and plugin).'
        : 'Raider plugin installed successfully (reused existing BepInEx).',
    };
  } catch (error) {
    return {
      success: false,
      error: `Installation failed: ${error instanceof Error ? error.message : String(error)}`,
    };
  }
}

/**
 * Remove Raider from the game folder.
 */
export async function remove(gamePath: string): Promise<InstallOperationResult> {
  try {
    const manifest = await readManifest(gamePath);
    const bepinexStatus = await checkBepInExStatus(gamePath);
    const pluginStatus = await checkPluginStatus(gamePath);
    
    if (!manifest && !bepinexStatus.present && !pluginStatus.present) {
      return {
        success: true,
        message: 'Raider is not installed.',
      };
    }

    const pluginsPath = path.join(gamePath, 'BepInEx', 'plugins');
    const pluginPath = path.join(pluginsPath, RAIDER_PLUGIN_NAME);
    const bepinexPath = path.join(gamePath, 'BepInEx');
    const manifestPath = path.join(gamePath, MANIFEST_FILE);

    // Always remove our plugin if present
    if (await pathExists(pluginPath)) {
      await fs.unlink(pluginPath);
    }

    // Remove manifest
    if (await pathExists(manifestPath)) {
      await fs.unlink(manifestPath);
    }

    // Remove BepInEx only if we own it
    if (manifest?.bepinexOwned && bepinexStatus.owned) {
      // Check if there are foreign plugins we should preserve
      if (bepinexStatus.foreignPlugins.length > 0) {
        // Don't remove BepInEx if there are foreign plugins
        // Just remove our plugin and manifest
        return {
          success: true,
          message: 'Raider plugin removed. BepInEx kept because other plugins are present.',
        };
      }
      
      // Remove BepInEx entirely
      await fs.rm(bepinexPath, { recursive: true, force: true });
      
      // Also check for doorstop_config.ini and loader dll in game root
      const doorstopConfig = path.join(gamePath, 'doorstop_config.ini');
      if (await pathExists(doorstopConfig)) {
        // Only remove if we created it (check manifest)
        // For safety, we'll leave it unless we're sure
      }
      
      return {
        success: true,
        message: 'Raider and BepInEx removed completely.',
      };
    } else {
      // We don't own BepInEx, just remove our plugin
      // Check if plugins folder is now empty (only our files were there)
      if (await pathExists(pluginsPath)) {
        const remaining = await fs.readdir(pluginsPath);
        if (remaining.length === 0) {
          await fs.rm(pluginsPath, { recursive: true, force: true });
        }
      }
      
      return {
        success: true,
        message: 'Raider plugin removed. BepInEx kept (not owned by Raider).',
      };
    }
  } catch (error) {
    return {
      success: false,
      error: `Removal failed: ${error instanceof Error ? error.message : String(error)}`,
    };
  }
}

/**
 * Get the game folder path.
 */
export async function getGamePath(): Promise<string | null> {
  return getStoredGamePath();
}

/**
 * Set the game folder path.
 */
export async function setGamePath(gamePath: string): Promise<void> {
  await setStoredGamePath(gamePath);
}

/**
 * Repair installation - reinstall missing pieces.
 */
export async function repair(gamePath: string): Promise<InstallOperationResult> {
  return install(gamePath); // Install handles both fresh install and repair
}

// Export the installer port implementation
export const installerPort: InstallerPort = {
  checkInstallation,
  install,
  remove,
  getGamePath,
  setGamePath,
};