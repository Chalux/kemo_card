export const summary =
    '**example** — 示例 builtin 模块。调用前请先 import 并阅读 `.description`。';

export const description = `
- **\`ping(message?)\`** — 回显消息，用于验证 builtin 加载与编译链路。
  - \`message\` (string, 可选, 默认 \`'pong'\`): 要回显的文本。
  - 返回: \`{ ok: true, message: string }\`
`.trim();

export function ping(message: string = 'pong'): { ok: true; message: string } {
    if (typeof message !== 'string') {
        throw new TypeError('message must be a string');
    }

    return { ok: true, message };
}
