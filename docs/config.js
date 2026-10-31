// Public by design: every permission is enforced by row level security (server/schema.sql).
// Fill these in from Supabase > Project Settings > API. Never put the service key here.
export const SUPABASE_URL = "https://vtgsbgkjleyqlxgjdvqy.supabase.co";
export const SUPABASE_ANON_KEY = "sb_publishable_7-z7D1GwtQJumqAPFxlJlA_d3myO4gk";

export const RELEASES_URL = "https://github.com/whoissaaif/desk-widgets/releases/latest";
// Starts the download of the newest release's exe; every release must attach a file with this name.
export const DOWNLOAD_URL = RELEASES_URL + "/download/DeskWidgets.exe";
export const REPO_URL = "https://github.com/whoissaaif/desk-widgets";
