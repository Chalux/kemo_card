var __defProp = Object.defineProperty;
var __name = (target, value) => __defProp(target, "name", { value, configurable: true });

// src/builtins/example.mts
var summary = "**example** \u2014 \u793A\u4F8B builtin \u6A21\u5757\u3002\u8C03\u7528\u524D\u8BF7\u5148 import \u5E76\u9605\u8BFB `.description`\u3002";
var description = `
- **\`ping(message?)\`** \u2014 \u56DE\u663E\u6D88\u606F\uFF0C\u7528\u4E8E\u9A8C\u8BC1 builtin \u52A0\u8F7D\u4E0E\u7F16\u8BD1\u94FE\u8DEF\u3002
  - \`message\` (string, \u53EF\u9009, \u9ED8\u8BA4 \`'pong'\`): \u8981\u56DE\u663E\u7684\u6587\u672C\u3002
  - \u8FD4\u56DE: \`{ ok: true, message: string }\`
`.trim();
function ping(message = "pong") {
  if (typeof message !== "string") {
    throw new TypeError("message must be a string");
  }
  return { ok: true, message };
}
__name(ping, "ping");
export {
  description,
  ping,
  summary
};
