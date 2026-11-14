// Public by design: every permission is enforced by row level security (server/schema.sql).
// Fill these in from Supabase > Project Settings > API. Never put the service key here.
export const SUPABASE_URL = "https://vtgsbgkjleyqlxgjdvqy.supabase.co";
export const SUPABASE_ANON_KEY = "sb_publishable_7-z7D1GwtQJumqAPFxlJlA_d3myO4gk";

export const RELEASES_URL = "https://github.com/whoissaaif/desk-widgets/releases/latest";
// Starts the download of the newest release's exe; every release must attach a file with this name.
export const DOWNLOAD_URL = RELEASES_URL + "/download/DeskWidgets.exe";
// The installer from the same release (packaging/installer.iss); download.html offers it first.
export const SETUP_URL = RELEASES_URL + "/download/DeskWidgets-Setup.exe";
export const REPO_URL = "https://github.com/whoissaaif/desk-widgets";
// The Store listing: product id 9PML42WDP84Z, published as "DeskWidgets" by Saiff. A different
// app holds the name "Desk Widgets" with the space, which is why that one was unavailable to
// reserve -- do not search the Store by name to find this link, the other app matches it.
export const STORE_URL = "https://apps.microsoft.com/detail/9PML42WDP84Z";

// GoatCounter site code: the first label of its subdomain, so "deskwidgets" for
// deskwidgets.goatcounter.com. Empty means no analytics script is loaded at all, which is the
// state the privacy page describes when this is blank. Sign up at https://www.goatcounter.com.
export const ANALYTICS_CODE = "deskwidgets";
