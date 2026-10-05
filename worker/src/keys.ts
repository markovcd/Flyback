/** Where each file lives in R2. */
export const presetFile = (id: string): string => `presets/${id}`;
export const pluginFile = (id: string): string => `plugins/${id}`;
export const pluginPreview = (id: string): string => `plugins/${id}.preview`;
export const mediaFile = (id: string, suffix: string): string => `media/${id}${suffix}`;
