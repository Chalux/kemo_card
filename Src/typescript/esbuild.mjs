import { build } from 'esbuild';
import { existsSync, mkdirSync, readdirSync } from 'fs';
import { join } from 'path';

/** Agent 资源根目录名，对应 Resource/agents/<agentName>/ */
const agentName = 'default-agent';

const builtinSrcDir = 'src/builtins';
const builtinOutDir = join('..', '..', 'Resource', 'agents', agentName, 'builtins');

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
