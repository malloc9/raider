/**
 * Installer tests against temporary game folders.
 * Covers: clean folder; compatible BepInEx with foreign plugins (reused, foreign plugins untouched);
 * BepInEx_disable* folders (untouched); incompatible BepInEx (refused with a clear message)
 */

import * as fs from 'fs/promises';
import * as path from 'path';
import { tmpdir } from 'os';
import { 
  checkInstallation, 
  install, 
  remove, 
  readManifest, 
  writeManifest 
} from '../main/installer';
import type { InstallManifest } from '../shared/types';

async function pathExists(p: string): Promise<boolean> {
  try {
    await fs.access(p);
    return true;
  } catch {
    return false;
  }
}

describe('Installer', () => {
  let testDir: string;
  let gamePath: string;

  beforeEach(async () => {
    testDir = await fs.mkdtemp(path.join(tmpdir(), 'raider-installer-test-'));
    gamePath = path.join(testDir, 'game');
    await fs.mkdir(gamePath, { recursive: true });
  });

  afterEach(async () => {
    await fs.rm(testDir, { recursive: true, force: true });
  });

  async function createBepInExStructure(gamePath: string, options: {
    withCore?: boolean;
    withPatchers?: boolean;
    withConfig?: boolean;
    withPlugins?: boolean;
    foreignPlugins?: string[];
    disableFolders?: string[];
  } = {}): Promise<void> {
    const bepinexPath = path.join(gamePath, 'BepInEx');
    
    if (options.withCore) {
      await fs.mkdir(path.join(bepinexPath, 'core'), { recursive: true });
      await fs.writeFile(path.join(bepinexPath, 'core', 'BepInEx.dll'), 'fake-bepinex-core');
    }
    if (options.withPatchers) {
      await fs.mkdir(path.join(bepinexPath, 'patchers'), { recursive: true });
      await fs.writeFile(path.join(bepinexPath, 'patchers', 'doorstop_libs.dll'), 'fake-patchers');
    }
    if (options.withConfig) {
      await fs.mkdir(path.join(bepinexPath, 'config'), { recursive: true });
      await fs.writeFile(path.join(bepinexPath, 'config', 'BepInEx.cfg'), 'fake-config');
    }
    if (options.withPlugins) {
      const pluginsPath = path.join(bepinexPath, 'plugins');
      await fs.mkdir(pluginsPath, { recursive: true });
      if (options.foreignPlugins) {
        for (const plugin of options.foreignPlugins) {
          await fs.writeFile(path.join(pluginsPath, plugin), `fake-${plugin}`);
        }
      }
    }
    
    if (options.disableFolders) {
      for (const folder of options.disableFolders) {
        await fs.mkdir(path.join(gamePath, folder), { recursive: true });
        await fs.writeFile(path.join(gamePath, folder, 'placeholder.txt'), 'disabled');
      }
    }
  }

  describe('checkInstallation', () => {
    it('returns not-installed for clean folder', async () => {
      const result = await checkInstallation(gamePath);
      
      expect(result.status).toBe('not-installed');
      expect(result.details?.bepinexPresent).toBe(false);
      expect(result.details?.pluginPresent).toBe(false);
      expect(result.details?.manifestPresent).toBe(false);
    });

    it('returns needs-repair when BepInEx present but plugin missing', async () => {
      await createBepInExStructure(gamePath, {
        withCore: true,
        withPatchers: true,
        withConfig: true,
        withPlugins: true,
      });
      
      const result = await checkInstallation(gamePath);
      
      expect(result.status).toBe('needs-repair');
      expect(result.details?.bepinexPresent).toBe(true);
      expect(result.details?.bepinexCompatible).toBe(true);
      expect(result.details?.pluginPresent).toBe(false);
    });

    it('returns installed when both BepInEx and plugin present with manifest', async () => {
      await createBepInExStructure(gamePath, {
        withCore: true,
        withPatchers: true,
        withConfig: true,
        withPlugins: true,
      });
      
      // Install via installer to create manifest
      await install(gamePath);
      
      const result = await checkInstallation(gamePath);
      
      expect(result.status).toBe('installed');
      expect(result.details?.bepinexPresent).toBe(true);
      expect(result.details?.pluginPresent).toBe(true);
      expect(result.details?.manifestPresent).toBe(true);
    });

    it('reuses compatible BepInEx and preserves foreign plugins', async () => {
      await createBepInExStructure(gamePath, {
        withCore: true,
        withPatchers: true,
        withConfig: true,
        withPlugins: true,
        foreignPlugins: ['PyAutoRaid.dll', 'OtherMod.dll'],
      });
      
      const checkBefore = await checkInstallation(gamePath);
      expect(checkBefore.details?.foreignPlugins).toContain('PyAutoRaid.dll');
      expect(checkBefore.details?.foreignPlugins).toContain('OtherMod.dll');
      
      const installResult = await install(gamePath);
      expect(installResult.success).toBe(true);
      
      const checkAfter = await checkInstallation(gamePath);
      expect(checkAfter.status).toBe('installed');
      expect(checkAfter.details?.foreignPlugins).toContain('PyAutoRaid.dll');
      expect(checkAfter.details?.foreignPlugins).toContain('OtherMod.dll');
      
      // Verify foreign plugins still exist
      const pluginsPath = path.join(gamePath, 'BepInEx', 'plugins');
      const files = await fs.readdir(pluginsPath);
      expect(files).toContain('PyAutoRaid.dll');
      expect(files).toContain('OtherMod.dll');
      expect(files).toContain('Raider.dll');
    });

    it('does not touch BepInEx_disable* folders', async () => {
      await createBepInExStructure(gamePath, {
        withCore: true,
        withPatchers: true,
        withConfig: true,
        withPlugins: true,
        disableFolders: ['BepInEx_disable_old', 'BepInEx_disable_test'],
      });
      
      const installResult = await install(gamePath);
      expect(installResult.success).toBe(true);
      
      // Verify disable folders still exist
      expect(await pathExists(path.join(gamePath, 'BepInEx_disable_old'))).toBe(true);
      expect(await pathExists(path.join(gamePath, 'BepInEx_disable_test'))).toBe(true);
    });

    it('refuses incompatible BepInEx (missing core)', async () => {
      await createBepInExStructure(gamePath, {
        withCore: false, // Missing core - incompatible
        withPatchers: true,
        withConfig: true,
        withPlugins: true,
      });
      
      const checkResult = await checkInstallation(gamePath);
      expect(checkResult.status).toBe('incompatible-bepinex');
      expect(checkResult.details?.bepinexCompatible).toBe(false);
      
      const installResult = await install(gamePath);
      expect(installResult.success).toBe(false);
      expect(installResult.error).toContain('Incompatible BepInEx');
    });

    it('refuses incompatible BepInEx (missing patchers)', async () => {
      await createBepInExStructure(gamePath, {
        withCore: true,
        withPatchers: false, // Missing patchers - incompatible
        withConfig: true,
        withPlugins: true,
      });
      
      const checkResult = await checkInstallation(gamePath);
      expect(checkResult.status).toBe('incompatible-bepinex');
      
      const installResult = await install(gamePath);
      expect(installResult.success).toBe(false);
    });

    it('refuses incompatible BepInEx (missing config)', async () => {
      await createBepInExStructure(gamePath, {
        withCore: true,
        withPatchers: true,
        withConfig: false, // Missing config - incompatible
        withPlugins: true,
      });
      
      const checkResult = await checkInstallation(gamePath);
      expect(checkResult.status).toBe('incompatible-bepinex');
      
      const installResult = await install(gamePath);
      expect(installResult.success).toBe(false);
    });
  });

  describe('install', () => {
    it('installs BepInEx and plugin in clean folder', async () => {
      const result = await install(gamePath);
      
      expect(result.success).toBe(true);
      
      // Check BepInEx structure created
      expect(await pathExists(path.join(gamePath, 'BepInEx', 'core'))).toBe(true);
      expect(await pathExists(path.join(gamePath, 'BepInEx', 'patchers'))).toBe(true);
      expect(await pathExists(path.join(gamePath, 'BepInEx', 'config'))).toBe(true);
      expect(await pathExists(path.join(gamePath, 'BepInEx', 'plugins'))).toBe(true);
      
      // Check plugin installed
      expect(await pathExists(path.join(gamePath, 'BepInEx', 'plugins', 'Raider.dll'))).toBe(true);
      
      // Check manifest created
      const manifest = await readManifest(gamePath);
      expect(manifest).not.toBeNull();
      expect(manifest?.bepinexOwned).toBe(true);
      expect(manifest?.files).toContain('BepInEx/plugins/Raider.dll');
    });

    it('reuses existing compatible BepInEx and only adds plugin', async () => {
      await createBepInExStructure(gamePath, {
        withCore: true,
        withPatchers: true,
        withConfig: true,
        withPlugins: true,
        foreignPlugins: ['ExistingMod.dll'],
      });
      
      const result = await install(gamePath);
      
      expect(result.success).toBe(true);
      expect(result.message).toContain('reused existing BepInEx');
      
      // Manifest should show bepinexOwned = false
      const manifest = await readManifest(gamePath);
      expect(manifest?.bepinexOwned).toBe(false);
      
      // Foreign plugin preserved
      expect(await pathExists(path.join(gamePath, 'BepInEx', 'plugins', 'ExistingMod.dll'))).toBe(true);
    });

    it('creates manifest recording all installed files and folders', async () => {
      await install(gamePath);
      
      const manifest = await readManifest(gamePath);
      expect(manifest).not.toBeNull();
      expect(manifest?.version).toBe(1);
      expect(manifest?.installedAt).toBeDefined();
      expect(typeof manifest?.bepinexOwned).toBe('boolean');
      expect(Array.isArray(manifest?.files)).toBe(true);
      expect(Array.isArray(manifest?.folders)).toBe(true);
      expect(manifest?.files.length).toBeGreaterThan(0);
      expect(manifest?.folders.length).toBeGreaterThan(0);
    });
  });

  describe('remove', () => {
    it('removes plugin and BepInEx when Raider owns BepInEx', async () => {
      await install(gamePath); // This creates BepInEx and owns it
      
      const result = await remove(gamePath);
      
      expect(result.success).toBe(true);
      expect(result.message).toContain('removed completely');
      
      // BepInEx should be gone
      expect(await pathExists(path.join(gamePath, 'BepInEx'))).toBe(false);
      // Manifest should be gone
      expect(await pathExists(path.join(gamePath, 'raider-install-manifest.json'))).toBe(false);
    });

    it('removes only plugin when BepInEx not owned by Raider', async () => {
      await createBepInExStructure(gamePath, {
        withCore: true,
        withPatchers: true,
        withConfig: true,
        withPlugins: true,
        foreignPlugins: ['OtherMod.dll'],
      });
      await install(gamePath); // Reuses BepInEx, doesn't own it
      
      const result = await remove(gamePath);
      
      expect(result.success).toBe(true);
      expect(result.message).toContain('kept (not owned by Raider)');
      
      // BepInEx should still exist
      expect(await pathExists(path.join(gamePath, 'BepInEx'))).toBe(true);
      // Foreign plugin should still exist
      expect(await pathExists(path.join(gamePath, 'BepInEx', 'plugins', 'OtherMod.dll'))).toBe(true);
      // Our plugin should be gone
      expect(await pathExists(path.join(gamePath, 'BepInEx', 'plugins', 'Raider.dll'))).toBe(false);
      // Manifest should be gone
      expect(await pathExists(path.join(gamePath, 'raider-install-manifest.json'))).toBe(false);
    });

    it('keeps BepInEx when foreign plugins present even if Raider owns BepInEx', async () => {
      await install(gamePath); // Raider owns BepInEx
      // Add a foreign plugin after install
      await fs.writeFile(path.join(gamePath, 'BepInEx', 'plugins', 'ForeignMod.dll'), 'foreign');
      
      const result = await remove(gamePath);
      
      expect(result.success).toBe(true);
      expect(result.message).toContain('other plugins are present');
      
      // BepInEx should still exist
      expect(await pathExists(path.join(gamePath, 'BepInEx'))).toBe(true);
      // Foreign plugin should still exist
      expect(await pathExists(path.join(gamePath, 'BepInEx', 'plugins', 'ForeignMod.dll'))).toBe(true);
    });

    it('handles removal when not installed gracefully', async () => {
      const result = await remove(gamePath);
      
      expect(result.success).toBe(true);
      expect(result.message).toContain('not installed');
    });
  });

  describe('repair (reinstall)', () => {
    it('reinstalls missing plugin when BepInEx present', async () => {
      await install(gamePath);
      // Delete the plugin
      await fs.unlink(path.join(gamePath, 'BepInEx', 'plugins', 'Raider.dll'));
      
      const checkBefore = await checkInstallation(gamePath);
      expect(checkBefore.status).toBe('needs-repair');
      
      const result = await install(gamePath); // Repair
      
      expect(result.success).toBe(true);
      expect(await pathExists(path.join(gamePath, 'BepInEx', 'plugins', 'Raider.dll'))).toBe(true);
      
      const checkAfter = await checkInstallation(gamePath);
      expect(checkAfter.status).toBe('installed');
    });

    it('reinstalls missing BepInEx files when Raider owns BepInEx', async () => {
      await install(gamePath);
      // Delete a core file
      await fs.unlink(path.join(gamePath, 'BepInEx', 'core', '.raider-placeholder'));
      
      const result = await install(gamePath); // Repair
      
      expect(result.success).toBe(true);
      expect(await pathExists(path.join(gamePath, 'BepInEx', 'core', '.raider-placeholder'))).toBe(true);
    });
  });

  describe('manifest', () => {
    it('records every file and folder Raider created', async () => {
      await install(gamePath);
      
      const manifest = await readManifest(gamePath);
      expect(manifest).not.toBeNull();
      
      // Verify all files in manifest exist
      for (const file of manifest!.files) {
        const fullPath = path.join(gamePath, file);
        expect(await pathExists(fullPath)).toBe(true);
      }
      
      // Verify all folders in manifest exist
      for (const folder of manifest!.folders) {
        const fullPath = path.join(gamePath, folder);
        expect(await pathExists(fullPath)).toBe(true);
      }
    });

    it('does not record foreign plugins or disable folders', async () => {
      await createBepInExStructure(gamePath, {
        withCore: true,
        withPatchers: true,
        withConfig: true,
        withPlugins: true,
        foreignPlugins: ['ForeignMod.dll'],
        disableFolders: ['BepInEx_disable_old'],
      });
      await install(gamePath);
      
      const manifest = await readManifest(gamePath);
      
      // Manifest should not contain foreign plugin
      expect(manifest?.files.some(f => f.includes('ForeignMod'))).toBe(false);
      // Manifest should not contain disable folders
      expect(manifest?.folders.some(f => f.includes('disable'))).toBe(false);
    });
  });
});