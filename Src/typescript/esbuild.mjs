import { build } from 'esbuild';
import { existsSync, mkdirSync, readdirSync, statSync } from 'fs';
import { join, relative } from 'path';

/** Agent 资源根目录名，对应 Resource/agents/<agentName>/ */
const agentName = 'default-agent';

const builtinSrcDir = 'src/builtins';
const builtinOutDir = join('..', '..', 'Resource', 'agents', agentName, 'builtins');

const modSrcRoot = 'src/mods';
const modOutRoot = join('..', '..', 'Config', 'mods');

function collectModEntryPoints(dir, rootDir = dir) {
    if (!existsSync(dir)) {
        return [];
    }

    const entries = [];
    for (const name of readdirSync(dir)) {
        const fullPath = join(dir, name);
        if (statSync(fullPath).isDirectory()) {
            entries.push(...collectModEntryPoints(fullPath, rootDir));
            continue;
        }

        if (name.endsWith('.mts') && !name.endsWith('.d.mts')) {
            entries.push(fullPath);
        }
    }

    return entries;
}

const builtinFiles = existsSync(builtinSrcDir)
    ? readdirSync(builtinSrcDir).filter(
          (file) => file.endsWith('.mts') && !file.endsWith('.d.mts'),
      )
    : [];

if (builtinFiles.length > 0) {
    if (!existsSync(builtinOutDir)) {
        mkdirSync(builtinOutDir, { recursive: true });
    }

    const builtinEntries = builtinFiles.map((file) => join(builtinSrcDir, file));

    await build({
        entryPoints: builtinEntries,
        bundle: true,
        format: 'esm',
        outdir: builtinOutDir,
        outExtension: { '.js': '.mjs' },
        platform: 'neutral',
        target: 'esnext',
        sourcemap: false,
        minify: false,
        keepNames: true,
        external: [],
        define: {
            'process.env.NODE_ENV': '"production"',
        },
    });

    console.log(
        `[esbuild:kemo-card-puerts] Built ${builtinFiles.length} builtins module(s) → ${builtinOutDir}/`,
    );
} else {
    console.log('[esbuild:kemo-card-puerts] No builtins modules found in src/builtins/');
}

const modEntryPoints = collectModEntryPoints(modSrcRoot);
if (modEntryPoints.length > 0) {
    for (const entryPoint of modEntryPoints) {
        const relFromMods = relative(modSrcRoot, entryPoint);
        const [modFolder, ...restParts] = relFromMods.split(/[\\/]/);
        const relScriptPath = restParts.join('/').replace(/\.mts$/, '.js');
        const outdir = join(modOutRoot, modFolder, 'scripts', ...restParts.slice(0, -1));
        const outfile = join(outdir, restParts.at(-1).replace(/\.mts$/, '.js'));

        if (!existsSync(outdir)) {
            mkdirSync(outdir, { recursive: true });
        }

        await build({
            entryPoints: [entryPoint],
            bundle: true,
            format: 'esm',
            outfile,
            platform: 'neutral',
            target: 'esnext',
            sourcemap: false,
            minify: false,
            keepNames: true,
            external: [],
            define: {
                'process.env.NODE_ENV': '"production"',
            },
        });

        console.log(
            `[esbuild:kemo-card-puerts] Built mod script ${modFolder}/${relScriptPath} → ${outfile}`,
        );
    }
} else {
    console.log('[esbuild:kemo-card-puerts] No mod scripts found in src/mods/');
}
