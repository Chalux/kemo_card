var __defProp = Object.defineProperty;
var __name = (target, value) => __defProp(target, "name", { value, configurable: true });

// src/mods/base-game/effects/script_demo.mts
function execute(ctx) {
  const amount = ctx.nextInt(4, 8);
  return {
    proposedEffects: [{ kind: "Damage", params: { amount } }]
  };
}
__name(execute, "execute");
export {
  execute
};
