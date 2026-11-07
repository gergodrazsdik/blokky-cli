/* Prints argv[0] and /proc/self/exe; switches on basename(argv[0]) like a multi-call binary. */
#include <stdio.h>
#include <string.h>
#include <unistd.h>
int main(int argc, char **argv) {
  char exe[4096] = {0};
  ssize_t n = readlink("/proc/self/exe", exe, sizeof exe - 1);
  if (n < 0) strcpy(exe, "?");
  const char *base = strrchr(argv[0], '/');
  base = base ? base + 1 : argv[0];
  const char *applet = strcmp(base, "hello") == 0 ? "hello-applet"
                     : strcmp(base, "bye") == 0 ? "bye-applet" : "unknown-applet";
  printf("argv0=%s exe=%s applet=%s argc=%d\n", argv[0], exe, applet, argc);
  return 0;
}
