import { build } from 'esbuild';
import { existsSync, mkdirSync, readdirSync, statSync } from 'fs';
import { dirname, join, relative } from 'path';

/** Agent 资源根目录名，对应 Resource/agents/<agentName>/ */
const agentName = 'default-agent';

const builtinSrcDir = 'src/builtins';
const builtinOutDir = join('..', '..', 'Resource', 'agents', agentName, 'builtins');

const modRoot = join('..', '..', 'Config', 'mods');
const modSrcDirName = 'scripts-src';
const modOutDirName = 'scripts';

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

const modFolderNames = existsSync(modRoot)
    ? readdirSync(modRoot).filter((name) =>
          existsSync(join(modRoot, name, modSrcDirName)),
      )
    : [];

for (const modFolder of modFolderNames) {
    const srcDir = join(modRoot, modFolder, modSrcDirName);
    const entryPoints = collectModEntryPoints(srcDir);
    if (entryPoints.length === 0) {
        continue;
    }

    for (const entryPoint of entryPoints) {
        const relScriptPath = relative(srcDir, entryPoint);
        const outfile = join(
            modRoot,
            modFolder,
            modOutDirName,
            relScriptPath.replace(/\.mts$/, '.js'),
        );

        if (!existsSync(dirname(outfile))) {
            mkdirSync(dirname(outfile), { recursive: true });
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
            `[esbuild:kemo-card-puerts] Built mod script ${modFolder}/${relScriptPath.replace(/\.mts$/, '.js')} → ${outfile}`,
        );
    }
}

if (modFolderNames.length === 0) {
    console.log(
        `[esbuild:kemo-card-puerts] No mod scripts found in ${modRoot}/*/${modSrcDirName}/`,
    );
}
