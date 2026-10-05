/** Where each file lives in R2. */
export const presetFile = (id: string): string => `presets/${id}`;
export const pluginFile = (id: string): string => `plugins/${id}`;
export const pluginPreview = (id: string): string => `plugins/${id}.preview`;
export const mediaFile = (id: string, suffix: string): string => `media/${id}${suffix}`;

/** A framework file past the 25 MiB a static asset may be, by its path under the site. */
export const largeAsset = (path: string): string => `assets/${path}`;
