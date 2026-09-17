extern int printf(readonly char* fmt, ...);

struct Cool
{
	int x;
	~Cool()
	{
		printf("destructor ran");
	}
}
int main()
{
	Cool* l = new* Cool();
	defer delete l;
	l.x = 2;
    printf("The number is: %d\n", 2);
	return 0;
}