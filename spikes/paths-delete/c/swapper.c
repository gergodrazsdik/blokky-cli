/* Swaps two names atomically (RENAME_EXCHANGE) in a tight loop until killed. */
#define _GNU_SOURCE
#include <fcntl.h>
#include <stdio.h>
int main(int argc, char **argv) {
  if (argc != 3) return 2;
  unsigned long ok = 0;
  for (;;) {
    if (renameat2(AT_FDCWD, argv[1], AT_FDCWD, argv[2], RENAME_EXCHANGE) == 0) ok++;
    else if (ok == 0) { /* names gone: tree removed */ }
  }
}
